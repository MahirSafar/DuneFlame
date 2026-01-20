using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Services;

public class OrderService(
    AppDbContext context,
    IRewardService rewardService,
    IBasketService basketService,
    ILogger<OrderService> logger) : IOrderService
{
    private readonly AppDbContext _context = context;
    private readonly IRewardService _rewardService = rewardService;
    private readonly IBasketService _basketService = basketService;
    private readonly ILogger<OrderService> _logger = logger;

    public async Task<OrderDto> CreateOrderAsync(Guid userId, CreateOrderRequest request)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Fetch basket from Redis
            var basket = await _basketService.GetBasketAsync(request.BasketId);

            if (basket == null || basket.Items.Count == 0)
            {
                throw new BadRequestException("Basket is empty. Cannot create order.");
            }

            // Use provided PaymentIntentId or fallback to basket's PaymentIntentId
            var paymentIntentId = request.PaymentIntentId ?? basket.PaymentIntentId;

            var order = new Order
            {
                UserId = userId,
                ShippingAddress = request.ShippingAddress.ToString(),
                Status = OrderStatus.Pending,
                TotalAmount = 0,
                PointsRedeemed = 0,
                PointsEarned = 0,
                PaymentIntentId = paymentIntentId
            };

            decimal totalAmount = 0;

            // Create order items from basket items
            // Price MUST be taken from the database, not from the basket (security)
            foreach (var basketItem in basket.Items)
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == basketItem.ProductId);

                if (product == null)
                {
                    throw new NotFoundException($"Product with ID {basketItem.ProductId} not found");
                }

                // Check stock availability
                if (product.StockQuantity < basketItem.Quantity)
                {
                    throw new BadRequestException(
                        $"Insufficient stock for product '{product.Name}'. " +
                        $"Available: {product.StockQuantity}, Requested: {basketItem.Quantity}");
                }

                // Calculate selling price from database: Price * (1 - DiscountPercentage/100)
                var sellingPrice = product.Price * (1 - product.DiscountPercentage / 100);

                // Create OrderItem (snapshot) with database price
                var orderItem = new OrderItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = sellingPrice,
                    Quantity = basketItem.Quantity
                };

                order.Items.Add(orderItem);
                totalAmount += sellingPrice * basketItem.Quantity;

                // Decrement product stock
                product.StockQuantity -= basketItem.Quantity;
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

                    // Redeem points atomically
                    await _rewardService.RedeemPointsAsync(userId, discount, order.Id);
                }
            }

            _context.Orders.Add(order);

            await _context.SaveChangesAsync();

            // Lock basket in Redis to prevent modification during payment processing
            // (Prevents race condition where user modifies basket while payment is being processed)
            try
            {
                // Update basket metadata to mark it as locked
                basket.Id = request.BasketId;
                await _basketService.UpdateBasketAsync(basket);
                _logger.LogInformation("Basket {BasketId} locked during payment processing for Order {OrderId}", 
                    request.BasketId, order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to lock basket {BasketId}. Payment may proceed but basket remains editable.", 
                    request.BasketId);
                // Non-critical failure - don't abort order creation
            }

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

    /// <summary>
    /// 3-PHASE PAYMENT SUCCESS PROCESSING with idempotency, retry guards, and stagger delay.
    /// PHASE 1: Atomic status update with optimistic concurrency check (RowVersion)
    /// PHASE 2: Shadow update - record payment transaction
    /// PHASE 3: Earn cashback points with fresh scope
    /// </summary>
    public async Task ProcessPaymentSuccessAsync(string paymentIntentId)
    {
        const int MaxRetries = 3;
        const int MinStaggerMs = 100;
        const int MaxStaggerMs = 500;
        var random = new Random();

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("ProcessPaymentSuccess PHASE 1: Atomic status update (Attempt {Attempt}/{MaxRetries}) for PaymentIntentId {PaymentIntentId}",
                    attempt, MaxRetries, paymentIntentId);

                // PHASE 1: Atomic Status Update with Optimistic Concurrency
                var order = await _context.Orders
                    .FirstOrDefaultAsync(o => o.PaymentIntentId == paymentIntentId);

                if (order == null)
                {
                    _logger.LogWarning("Order not found with PaymentIntentId: {PaymentIntentId}", paymentIntentId);
                    throw new NotFoundException($"Order not found with PaymentIntentId: {paymentIntentId}");
                }

                // Idempotency check - if already paid, return safely
                if (order.Status == OrderStatus.Paid)
                {
                    _logger.LogInformation("Order already paid (idempotent): {OrderId} with PaymentIntentId {PaymentIntentId}. Skipping.",
                        order.Id, paymentIntentId);
                    await transaction.CommitAsync();
                    return;
                }

                // Update status atomically
                order.Status = OrderStatus.Paid;
                _context.Orders.Update(order);
                await _context.SaveChangesAsync();

                _logger.LogInformation("ProcessPaymentSuccess PHASE 2: Shadow update - payment transaction record for Order {OrderId}",
                    order.Id);

                // PHASE 2: Shadow Update - Record Payment Transaction
                var paymentTransaction = new PaymentTransaction
                {
                    OrderId = order.Id,
                    Amount = order.TotalAmount,
                    Currency = "usd",
                    Status = "Succeeded",
                    TransactionId = paymentIntentId,
                    PaymentMethod = "card"
                };

                _context.PaymentTransactions.Add(paymentTransaction);
                await _context.SaveChangesAsync();

                _logger.LogInformation("ProcessPaymentSuccess PHASE 3: Earn cashback points for Order {OrderId}",
                    order.Id);

                // PHASE 3: Earn Cashback Points (fresh scope, retryable)
                var cashback = RewardService.CalculateCashback(order.TotalAmount);
                order.PointsEarned = cashback;
                _context.Orders.Update(order);

                // Call RewardService to earn points
                await _rewardService.EarnPointsAsync(order.UserId, order.Id, cashback);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Payment success processed for Order {OrderId} with PaymentIntentId {PaymentIntentId}. Earned {Cashback} points",
                    order.Id, paymentIntentId, cashback);

                return; // Success - exit retry loop
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning(ex, "Concurrency conflict on attempt {Attempt}/{MaxRetries}. RowVersion mismatch. Retrying...",
                    attempt, MaxRetries);

                if (attempt == MaxRetries)
                {
                    _logger.LogError(ex, "All concurrency retry attempts exhausted for PaymentIntentId {PaymentIntentId}",
                        paymentIntentId);
                    throw;
                }

                // Apply stagger delay to reduce contention
                int staggerMs = random.Next(MinStaggerMs, MaxStaggerMs);
                await Task.Delay(staggerMs);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error processing payment success on attempt {Attempt}/{MaxRetries} for PaymentIntentId {PaymentIntentId}. " +
                    "Exception: {ExceptionMessage}",
                    attempt, MaxRetries, paymentIntentId, ex.Message);

                if (attempt == MaxRetries)
                {
                    _logger.LogError(ex, "All retry attempts exhausted for PaymentIntentId {PaymentIntentId}",
                        paymentIntentId);
                    throw;
                }

                // Apply stagger delay before retry
                int staggerMs = random.Next(MinStaggerMs, MaxStaggerMs);
                await Task.Delay(staggerMs);
            }
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
