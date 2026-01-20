using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

public class AdminOrderService(
    AppDbContext context,
    IPaymentService paymentService,
    IRewardService rewardService,
    IEmailService emailService,
    IServiceProvider serviceProvider,
    ILogger<AdminOrderService> logger) : IAdminOrderService
{
    private readonly AppDbContext _context = context;
    private readonly IPaymentService _paymentService = paymentService;
    private readonly IRewardService _rewardService = rewardService;
    private readonly IEmailService _emailService = emailService;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<AdminOrderService> _logger = logger;

    /// <summary>
    /// Validates if a status transition is allowed according to business rules.
    /// </summary>
    private static void ValidateStatusTransition(OrderStatus currentStatus, OrderStatus newStatus)
    {
        // Check if order is in a final state
        if (currentStatus == OrderStatus.Delivered || currentStatus == OrderStatus.Cancelled)
        {
            throw new BadRequestException("Cannot change status of a finalized order.");
        }

        // Define valid transitions
        var validTransitions = new Dictionary<OrderStatus, HashSet<OrderStatus>>
        {
            { OrderStatus.Pending, new HashSet<OrderStatus> { OrderStatus.Paid, OrderStatus.Cancelled } },
            { OrderStatus.Paid, new HashSet<OrderStatus> { OrderStatus.Shipped, OrderStatus.Cancelled } },
            { OrderStatus.Shipped, new HashSet<OrderStatus> { OrderStatus.Delivered, OrderStatus.Cancelled } },
            { OrderStatus.Delivered, new HashSet<OrderStatus>() }, // No transitions allowed
            { OrderStatus.Cancelled, new HashSet<OrderStatus>() }  // No transitions allowed
        };

        if (!validTransitions.TryGetValue(currentStatus, out var allowedStatuses) || !allowedStatuses.Contains(newStatus))
        {
            throw new BadRequestException("Invalid status transition.");
        }
    }

     public async Task<PagedResult<OrderDto>> GetAllOrdersAsync(
         int pageNumber = 1,
         int pageSize = 10,
         OrderStatus? status = null,
         string? searchTerm = null)
     {
         try
         {
             // Validation
             if (pageNumber < 1) pageNumber = 1;
             if (pageSize < 1) pageSize = 10;
             if (pageSize > 100) pageSize = 100; // Max page size

             var query = _context.Orders
                 .Include(o => o.Items)
                 .Include(o => o.ApplicationUser)
                 .Include(o => o.PaymentTransactions)
                 .AsSplitQuery()
                 .AsQueryable();

             // Filtering by status
             if (status.HasValue)
             {
                 query = query.Where(o => o.Status == status.Value);
             }

             // Filtering by search term (Order ID or User Name/Email)
             if (!string.IsNullOrWhiteSpace(searchTerm))
             {
                 query = query.Where(o =>
                     o.Id.ToString().Contains(searchTerm) ||
                     (o.ApplicationUser != null && (
                         o.ApplicationUser.UserName.Contains(searchTerm) ||
                         o.ApplicationUser.Email.Contains(searchTerm))));
             }

             // Sort by creation date (newest first)
             query = query.OrderByDescending(o => o.CreatedAt);

             // Pagination
             var totalCount = await query.CountAsync();
             var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

             var orders = await query
                 .Skip((pageNumber - 1) * pageSize)
                 .Take(pageSize)
                 .ToListAsync();

             var orderDtos = orders.Select(MapToOrderDto).ToList();

             return new PagedResult<OrderDto>(
                 orderDtos,
                 totalCount,
                 pageNumber,
                 pageSize,
                 totalPages
             );
         }
         catch (Exception ex)
         {
             _logger.LogError(ex, "Error retrieving orders with pagination");
             throw;
         }
     }

    public async Task UpdateOrderStatusAsync(Guid orderId, OrderStatus status)
    {
        try
        {
            var order = await _context.Orders
                .Include(o => o.ApplicationUser)
                .FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null)
            {
                throw new NotFoundException($"Order not found: {orderId}");
            }

            // Validate status transition
            ValidateStatusTransition(order.Status, status);

            order.Status = status;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Order {OrderId} status updated to {Status}", orderId, status);

            // Send email based on status change
            var userEmail = order.ApplicationUser?.Email;
            if (!string.IsNullOrEmpty(userEmail))
            {
                try
                {
                    switch (status)
                    {
                        case OrderStatus.Shipped:
                            await _emailService.SendOrderShippedAsync(userEmail, orderId);
                            _logger.LogInformation("Shipped email sent for Order {OrderId}", orderId);
                            break;
                        case OrderStatus.Delivered:
                            await _emailService.SendOrderDeliveredAsync(userEmail, orderId);
                            _logger.LogInformation("Delivered email sent for Order {OrderId}", orderId);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send status update email for Order {OrderId} with status {Status}",
                        orderId, status);
                    // Non-critical failure - don't throw
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order status: {OrderId}", orderId);
            throw;
        }
    }

    /// <summary>
    /// 3-PHASE ORDER CANCELLATION with Shadow Updates, Idempotency, and Race Condition Guards.
    /// 
    /// PHASE 1 (ATOMIC STATUS): Update order status to Cancelled (optimistic concurrency via RowVersion)
    /// PHASE 2 (SHADOW UPDATES): Native SQL for refunds, inventory, reward adjustments (atomic, no ORM conflicts)
    /// PHASE 3 (ASYNC EMAIL): Send cancellation email via fresh service scope (retryable, failure-resilient)
    /// </summary>
    public async Task CancelOrderAsync(Guid orderId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            _logger.LogInformation("CancelOrderAsync PHASE 1: Atomic status update for Order {OrderId}", orderId);

            // PHASE 1: Atomic Status Update with Optimistic Concurrency Check (RowVersion)
            var order = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.PaymentTransactions)
                .AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                throw new NotFoundException($"Order not found: {orderId}");
            }

            // Check if order is already delivered - cannot cancel
            if (order.Status == OrderStatus.Delivered)
            {
                throw new BadRequestException("Cannot cancel a delivered order.");
            }

            // Idempotency check
            if (order.Status == OrderStatus.Cancelled)
            {
                _logger.LogInformation("Order {OrderId} already cancelled (idempotent). Skipping.", orderId);
                await transaction.CommitAsync();
                return;
            }

            // CRITICAL: Capture original status BEFORE modifying the order object
            // This is needed for Phase 2A refund logic because we set Status = Cancelled here,
            // which would cause the refund condition check to always fail if using order.Status
            var originalStatus = order.Status;

            // Update order status atomically
            order.Status = OrderStatus.Cancelled;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            _logger.LogInformation("CancelOrderAsync PHASE 2: Shadow updates via native SQL for Order {OrderId}", orderId);

            // CRITICAL: Clear ALL tracked entities (Order, Items, Transactions) to prevent "Ghost Entity" conflicts in Phase 2
            // This ensures that OrderItem and PaymentTransaction entities don't cause concurrency exceptions
            // when SaveChangesAsync is called during reward refund processing.
            _context.ChangeTracker.Clear();
            _logger.LogInformation("ChangeTracker cleared after Phase 1 commit to enable clean Phase 2 operations for Order {OrderId}", orderId);

            // PHASE 2: Shadow Updates - Native SQL (atomic, avoids ORM tracking conflicts)
            // Set short command timeout to prevent hanging on rare DB locks
            _context.Database.SetCommandTimeout(TimeSpan.FromSeconds(5));

            try
            {
                // 2A: Handle Payment Refund via native SQL
                // Check original status because order.Status is already 'Cancelled' at this point
                // Support refunds for both Paid and Shipped orders
                if (originalStatus == OrderStatus.Paid || originalStatus == OrderStatus.Shipped)
                {
                    var paymentTransaction = order.PaymentTransactions.FirstOrDefault(pt => pt.Status == "Succeeded");
                    if (paymentTransaction != null && string.IsNullOrEmpty(paymentTransaction.RefundId))
                    {
                        try
                        {
                            var refundResponse = await _paymentService.RefundPaymentAsync(
                                paymentTransaction.TransactionId, 
                                paymentTransaction.Amount);

                            // Update RefundId via native SQL to prevent ORM conflicts
                            // Extract the RefundId from the response object
                            await _context.Database.ExecuteSqlInterpolatedAsync(
                                $@"UPDATE ""PaymentTransactions"" 
                                   SET ""RefundId"" = {refundResponse.RefundId}, ""Status"" = 'Refunded' 
                                   WHERE ""Id"" = {paymentTransaction.Id}");

                            _logger.LogInformation("Payment refunded for Order {OrderId}: {Amount} (RefundId: {RefundId})", 
                                orderId, paymentTransaction.Amount, refundResponse.RefundId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to refund payment for Order {OrderId}", orderId);
                            throw;
                        }
                    }
                }

                // 2B: Restock Inventory via native SQL
                foreach (var item in order.Items)
                {
                    try
                    {
                        await _context.Database.ExecuteSqlInterpolatedAsync(
                            $@"UPDATE ""Products"" 
                               SET ""StockQuantity"" = ""StockQuantity"" + {item.Quantity} 
                               WHERE ""Id"" = {item.ProductId}");

                    _logger.LogInformation("Restocked {Quantity} units of Product {ProductId}", 
                        item.Quantity, item.ProductId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restock inventory for Product {ProductId}", item.ProductId);
                    throw;
                }
            }

            // 2C: Reverse Reward Points via Unit of Work Pattern
            if (order.PointsEarned > 0 || order.PointsRedeemed > 0)
            {
                try
                {
                    // Pass only primitives to prevent EF Core from tracking the stale Order entity
                    await _rewardService.RefundPointsAsync(order.UserId, order.Id, order.PointsEarned, order.PointsRedeemed);
                    _logger.LogInformation("Reward points reversed for Order {OrderId}", orderId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to reverse reward points for Order {OrderId}", orderId);
                    throw;
                }
            }

            // Save all shadow updates together
            await _context.SaveChangesAsync();
            }
            finally
            {
                // Reset command timeout to default
                _context.Database.SetCommandTimeout(null);
            }

            await transaction.CommitAsync();

            _logger.LogInformation("CancelOrderAsync PHASE 3: Async email notification for Order {OrderId}", orderId);

            // PHASE 3: Email Notification (Async, Fire-and-Forget, Failure-Resilient)
            try
            {
                // Use fresh service scope to avoid DbContext lifetime issues
                // Send email asynchronously without blocking the main operation
                var userEmail = order.ApplicationUser?.Email ?? string.Empty;
                if (!string.IsNullOrEmpty(userEmail))
                {
                    // Fire-and-forget: Queue email in background without awaiting
                    // This ensures cancellation response is not delayed by email service
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                        // Async send without await (fire-and-forget)
                        #pragma warning disable CS4014 // Because this call is not awaited, execution continues
                        emailService.SendOrderCancelledAsync(userEmail, order.Id, order.TotalAmount).ContinueWith(task =>
                        {
                            if (task.IsFaulted)
                            {
                                _logger.LogWarning(task.Exception, "Failed to send cancellation email for Order {OrderId}", orderId);
                            }
                            else
                            {
                                _logger.LogInformation("Cancellation email sent successfully for Order {OrderId}", orderId);
                            }
                        });
                        #pragma warning restore CS4014
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error initiating email notification for Order {OrderId}", orderId);
                    }
                }
            }
            catch (Exception ex)
            {
                // Phase 3 failure is non-critical
                _logger.LogWarning(ex, "Error initiating email notification for Order {OrderId}. Order cancellation succeeded.", orderId);
            }

            _logger.LogInformation("Order {OrderId} successfully cancelled (3-Phase complete)", orderId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error cancelling order {OrderId}. Transaction rolled back", orderId);
            throw;
        }
    }

     private static OrderDto MapToOrderDto(Order order)
     {
         var orderItems = order.Items.Select(oi => new OrderItemDto(
             oi.Id,
             oi.ProductId,
             oi.ProductName,
             oi.UnitPrice,
             oi.Quantity
         )).ToList();

         // Extract customer details with null safety
         var customerName = order.ApplicationUser != null
             ? (string.IsNullOrWhiteSpace(order.ApplicationUser.FirstName) 
                 ? order.ApplicationUser.UserName 
                 : $"{order.ApplicationUser.FirstName} {order.ApplicationUser.LastName}".Trim())
             : "Unknown Customer";

         var customerEmail = order.ApplicationUser?.Email ?? "No Email";
         var customerPhone = order.ApplicationUser?.PhoneNumber ?? "No Phone";
         var shippingAddress = order.ShippingAddress ?? "No Address";

         // Get the most recent successful payment transaction ID
         var paymentTransactionId = order.PaymentTransactions
             .Where(pt => pt.Status == "Succeeded")
             .OrderByDescending(pt => pt.CreatedAt)
             .FirstOrDefault()?.TransactionId;

         return new OrderDto(
             order.Id,
             order.Status,
             order.TotalAmount,
             order.CreatedAt,
             shippingAddress,
             customerName,
             customerEmail,
             customerPhone,
             paymentTransactionId,
             orderItems
         );
     }
}
