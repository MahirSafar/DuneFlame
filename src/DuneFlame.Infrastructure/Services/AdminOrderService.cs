using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

public class AdminOrderService(
    AppDbContext context,
    IPaymentService paymentService,
    IRewardService rewardService,
    ILogger<AdminOrderService> logger) : IAdminOrderService
{
    private readonly AppDbContext _context = context;
    private readonly IPaymentService _paymentService = paymentService;
    private readonly IRewardService _rewardService = rewardService;
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
            var order = await _context.Orders.FindAsync(orderId);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating order status: {OrderId}", orderId);
            throw;
        }
    }

    public async Task CancelOrderAsync(Guid orderId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // 1. Get Order with related data
            var order = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.PaymentTransactions)
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

            _logger.LogInformation("Starting cancellation for Order {OrderId}", orderId);

            // 2. Handle Stripe refund if order is paid
            if (order.Status == OrderStatus.Paid)
            {
                var paymentTransaction = order.PaymentTransactions.FirstOrDefault(pt => pt.Status == "Succeeded");
                if (paymentTransaction != null)
                {
                    try
                    {
                        await _paymentService.RefundPaymentAsync(paymentTransaction.TransactionId, paymentTransaction.Amount);
                        paymentTransaction.Status = "Refunded";
                        // NOTE: Do NOT call Update() - entity is already tracked via Include
                        _logger.LogInformation("Payment refunded for Order {OrderId}: {Amount}", orderId, paymentTransaction.Amount);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to refund payment for Order {OrderId}", orderId);
                        throw;
                    }
                }
            }

            // 3. Reverse reward points
            if (order.PointsEarned > 0 || order.PointsRedeemed > 0)
            {
                try
                {
                    // Pass the order object directly (Unit of Work pattern)
                    // RefundPointsAsync modifies entities but does NOT save
                    await _rewardService.RefundPointsAsync(order);
                    _logger.LogInformation("Reward points refunded for Order {OrderId}", orderId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refund reward points for Order {OrderId}", orderId);
                    throw;
                }
            }

            // 4. Restock inventory
            foreach (var item in order.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product != null)
                {
                    product.StockQuantity += item.Quantity;
                    // NOTE: Do NOT call Update() - entity is already tracked via FindAsync
                    _logger.LogInformation("Restocked {Quantity} units of Product {ProductId}", item.Quantity, item.ProductId);
                }
            }

            // 5. Update order status
            order.Status = OrderStatus.Cancelled;
            // NOTE: Do NOT call Update() - entity is already tracked via initial Include
            // Single SaveChangesAsync will persist all accumulated changes

            // Attempt to save with concurrency conflict recovery
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Concurrency conflict detected
                // Since we just processed an external Stripe refund, we MUST persist the cancellation
                // Reload conflicting entities and re-apply our changes
                _logger.LogWarning(ex, "Concurrency conflict detected while cancelling Order {OrderId}, recovering...", orderId);

                // Iterate through entities that failed due to concurrency
                foreach (var entry in ex.Entries)
                {
                    // Reload the latest values from database
                    await entry.ReloadAsync();

                    // Re-apply our changes based on entity type
                    switch (entry.Entity)
                    {
                        case Order o:
                            // Force cancellation state
                            o.Status = OrderStatus.Cancelled;
                            _logger.LogInformation("Re-applied Order cancellation for {OrderId}", orderId);
                            break;

                        case Product p:
                            // Re-calculate: we added stock during restock operation
                            // Find the original quantity we were adding back
                            var orderItem = order.Items.FirstOrDefault(oi => oi.ProductId == p.Id);
                            if (orderItem != null)
                            {
                                p.StockQuantity += orderItem.Quantity;
                                _logger.LogInformation("Re-applied inventory restock for Product {ProductId}: +{Quantity}", p.Id, orderItem.Quantity);
                            }
                            break;

                        case PaymentTransaction pt:
                            // Force refund state (critical - payment was already processed)
                            pt.Status = "Refunded";
                            _logger.LogInformation("Re-applied payment refund status for Transaction {TransactionId}", pt.TransactionId);
                            break;

                        case RewardWallet w:
                            // Re-calculate: wallet balance was modified during points reversal
                            // Subtract earned points and add redeemed points (same logic as RefundPointsAsync)
                            w.Balance -= order.PointsEarned;
                            w.Balance += order.PointsRedeemed;
                            _logger.LogInformation("Re-applied reward points reversal for Order {OrderId}", orderId);
                            break;
                    }
                }

                // Retry the save after recovering
                try
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully recovered and saved after concurrency conflict for Order {OrderId}", orderId);
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "Failed to save after concurrency recovery for Order {OrderId}", orderId);
                    throw;
                }
            }

            await transaction.CommitAsync();

            _logger.LogInformation("Order {OrderId} successfully cancelled", orderId);
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
