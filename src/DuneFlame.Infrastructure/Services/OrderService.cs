using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class OrderService(AppDbContext context, IRewardService rewardService) : IOrderService
{
    private readonly AppDbContext _context = context;
    private readonly IRewardService _rewardService = rewardService;

    public async Task<OrderDto> CreateOrderAsync(Guid userId, CreateOrderRequest request)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(ci => ci.Product)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null || cart.Items.Count == 0)
            {
                throw new InvalidOperationException("Cart is empty. Cannot create order.");
            }

            var order = new Order
            {
                UserId = userId,
                ShippingAddress = request.ShippingAddress,
                Status = OrderStatus.Pending,
                TotalAmount = 0,
                PointsRedeemed = 0,
                PointsEarned = 0
            };

            decimal totalAmount = 0;

            foreach (var cartItem in cart.Items)
            {
                var product = cartItem.Product;
                if (product == null)
                {
                    throw new KeyNotFoundException($"Product with ID {cartItem.ProductId} not found");
                }

                // Check stock availability
                if (product.StockQuantity < cartItem.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Insufficient stock for product '{product.Name}'. " +
                        $"Available: {product.StockQuantity}, Requested: {cartItem.Quantity}");
                }

                // Create OrderItem (snapshot)
                var orderItem = new OrderItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = product.Price,
                    Quantity = cartItem.Quantity
                };

                order.Items.Add(orderItem);
                totalAmount += product.Price * cartItem.Quantity;

                // Decrement product stock
                product.StockQuantity -= cartItem.Quantity;
                _context.Products.Update(product);
            }

            order.TotalAmount = totalAmount;

            // Handle reward points redemption
            if (request.UsePoints)
            {
                var wallet = await _context.RewardWallets
                    .FirstOrDefaultAsync(w => w.UserId == userId);

                if (wallet != null && wallet.Balance > 0)
                {
                    // Calculate discount (min of balance or order total)
                    var discount = Math.Min(wallet.Balance, order.TotalAmount);
                    order.PointsRedeemed = discount;
                    order.TotalAmount -= discount;

                    // Redeem points
                    await _rewardService.RedeemPointsAsync(userId, discount, order.Id);
                }
            }

            _context.Orders.Add(order);

            // Clear cart
            cart.Items.Clear();

            await _context.SaveChangesAsync();

            // Calculate and earn cashback points (after order is created)
            var cashback = RewardService.CalculateCashback(order.TotalAmount);
            order.PointsEarned = cashback;
            await _rewardService.EarnPointsAsync(userId, order.Id, cashback);

            await transaction.CommitAsync();

            return MapToOrderDto(order);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<OrderDto>> GetMyOrdersAsync(Guid userId)
    {
        var orders = await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return orders.Select(MapToOrderDto).ToList();
    }

    public async Task<OrderDto> GetOrderByIdAsync(Guid id)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
        {
            throw new KeyNotFoundException($"Order with ID {id} not found");
        }

        return MapToOrderDto(order);
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
