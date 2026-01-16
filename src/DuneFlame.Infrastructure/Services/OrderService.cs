using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

public class OrderService(
    AppDbContext context,
    IRewardService rewardService,
    ILogger<OrderService> logger) : IOrderService
{
    private readonly AppDbContext _context = context;
    private readonly IRewardService _rewardService = rewardService;
    private readonly ILogger<OrderService> _logger = logger;

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
                throw new BadRequestException("Cart is empty. Cannot create order.");
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
                    throw new NotFoundException($"Product with ID {cartItem.ProductId} not found");
                }

                // Check stock availability
                if (product.StockQuantity < cartItem.Quantity)
                {
                    throw new BadRequestException(
                        $"Insufficient stock for product '{product.Name}'. " +
                        $"Available: {product.StockQuantity}, Requested: {cartItem.Quantity}");
                }

                // Calculate selling price: Price * (1 - DiscountPercentage/100)
                var sellingPrice = product.Price * (1 - product.DiscountPercentage / 100);

                // Create OrderItem (snapshot)
                var orderItem = new OrderItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = sellingPrice,
                    Quantity = cartItem.Quantity
                };

                order.Items.Add(orderItem);
                totalAmount += sellingPrice * cartItem.Quantity;

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
             await transaction.CommitAsync();

             _logger.LogInformation("Order created with ID {OrderId} for user {UserId}. Total: {TotalAmount}", 
                 order.Id, userId, order.TotalAmount);

             // Reload order with related data for mapping
             var createdOrder = await _context.Orders
                 .Include(o => o.Items)
                 .Include(o => o.ApplicationUser)
                 .FirstOrDefaultAsync(o => o.Id == order.Id);

             return MapToOrderDto(createdOrder!);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error creating order for user {UserId}", userId);
            throw;
        }
    }

    public async Task ProcessPaymentSuccessAsync(string transactionId)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var paymentTransaction = await _context.PaymentTransactions
                .Include(pt => pt.Order)
                .FirstOrDefaultAsync(pt => pt.TransactionId == transactionId);

            if (paymentTransaction == null)
            {
                _logger.LogWarning("Payment transaction not found: {TransactionId}", transactionId);
                throw new NotFoundException($"Payment transaction not found: {transactionId}");
            }

            // Idempotency check - if already processed, return
            if (paymentTransaction.Status == "Succeeded")
            {
                _logger.LogInformation("Payment already processed: {TransactionId}", transactionId);
                return;
            }

            // Update transaction status
            paymentTransaction.Status = "Succeeded";
            _context.PaymentTransactions.Update(paymentTransaction);

            // Update order status
            var order = paymentTransaction.Order;
            if (order != null)
            {
                order.Status = OrderStatus.Paid;
                _context.Orders.Update(order);

                // Earn cashback points AFTER payment success
                var cashback = RewardService.CalculateCashback(order.TotalAmount);
                order.PointsEarned = cashback;
                await _rewardService.EarnPointsAsync(order.UserId, order.Id, cashback);

                _logger.LogInformation(
                    "Payment succeeded for Order {OrderId}. Earned {Cashback} points",
                    order.Id, cashback);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error processing payment success: {TransactionId}", transactionId);
            throw;
        }
    }

     public async Task<List<OrderDto>> GetMyOrdersAsync(Guid userId)
     {
         var orders = await _context.Orders
             .Include(o => o.Items)
             .Include(o => o.ApplicationUser)
             .Where(o => o.UserId == userId)
             .OrderByDescending(o => o.CreatedAt)
             .ToListAsync();

         return orders.Select(MapToOrderDto).ToList();
     }

     public async Task<OrderDto> GetOrderByIdAsync(Guid id)
     {
         var order = await _context.Orders
             .Include(o => o.Items)
             .Include(o => o.ApplicationUser)
             .Include(o => o.PaymentTransactions)
             .FirstOrDefaultAsync(o => o.Id == id);

         if (order == null)
         {
             throw new NotFoundException($"Order with ID {id} not found");
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
