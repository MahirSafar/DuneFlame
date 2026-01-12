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

    public async Task<List<OrderDto>> GetAllOrdersAsync()
    {
        try
        {
            var orders = await _context.Orders
                .Include(o => o.Items)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            return orders.Select(MapToOrderDto).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all orders");
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
                        _context.PaymentTransactions.Update(paymentTransaction);
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
                    await _rewardService.RefundPointsAsync(order.UserId, orderId);
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
                    _context.Products.Update(product);
                    _logger.LogInformation("Restocked {Quantity} units of Product {ProductId}", item.Quantity, item.ProductId);
                }
            }

            // 5. Update order status
            order.Status = OrderStatus.Cancelled;
            _context.Orders.Update(order);

            await _context.SaveChangesAsync();
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

        return new OrderDto(order.Id, order.Status, order.TotalAmount, order.CreatedAt, orderItems);
    }
}
