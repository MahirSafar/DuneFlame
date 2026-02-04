using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.DTOs.Basket;
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
    ICurrencyProvider currencyProvider,
    IPaymentService paymentService,
    IShippingService shippingService,
    ILogger<OrderService> logger) : IOrderService
{
    private readonly AppDbContext _context = context;
    private readonly IRewardService _rewardService = rewardService;
    private readonly IBasketService _basketService = basketService;
    private readonly ICurrencyProvider _currencyProvider = currencyProvider;
    private readonly IPaymentService _paymentService = paymentService;
    private readonly IShippingService _shippingService = shippingService;
    private readonly ILogger<OrderService> _logger = logger;

    public async Task<OrderDto> CreateOrderAsync(Guid? userId, CreateOrderRequest request)
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

            // IDEMPOTENCY CHECK: If basket has a PaymentIntentId, check for existing Pending order
            // This handles the scenario where user cancels modal and clicks "Proceed" again
            if (!string.IsNullOrEmpty(basket.PaymentIntentId))
            {
                var existingOrder = await _context.Orders
                    .FirstOrDefaultAsync(o => 
                        o.PaymentIntentId == basket.PaymentIntentId && 
                        o.Status == OrderStatus.Pending);

                if (existingOrder != null)
                {
                    _logger.LogInformation(
                        "Idempotent return: Found existing Pending order {OrderId} with PaymentIntentId {PaymentIntentId}. Returning existing order for user {UserId}.",
                        existingOrder.Id, basket.PaymentIntentId, userId);

                    // Reload order with related data for mapping
                    var reloadedOrder = await _context.Orders
                        .Include(o => o.Items)
                        .Include(o => o.ApplicationUser)
                        .FirstOrDefaultAsync(o => o.Id == existingOrder.Id);

                    if (reloadedOrder != null)
                    {
                        // PHASE 9: Retrieve existing PaymentIntent (DO NOT regenerate)
                        // Only create new if PaymentIntentId is missing
                        PaymentIntentResponse idempotentPaymentIntent;

                        if (!string.IsNullOrEmpty(reloadedOrder.PaymentIntentId))
                        {
                            // RETRIEVE existing PaymentIntent - no new ID generation
                            _logger.LogInformation(
                                "Idempotent path: Retrieving existing PaymentIntent {PaymentIntentId} for Order {OrderId}",
                                reloadedOrder.PaymentIntentId, reloadedOrder.Id);

                            idempotentPaymentIntent = await _paymentService.GetPaymentIntentAsync(reloadedOrder.PaymentIntentId);
                        }
                        else
                        {
                            // PaymentIntentId is missing - this shouldn't happen if PaymentIntentId is persisted to Redis
                            // But handle it gracefully by creating new one
                            _logger.LogWarning(
                                "Idempotent path: PaymentIntentId missing for existing Order {OrderId}. Creating new PaymentIntent.",
                                reloadedOrder.Id);

                            idempotentPaymentIntent = await _paymentService.CreatePaymentIntentAsync(
                                reloadedOrder.TotalAmount,
                                reloadedOrder.CurrencyCode.ToString().ToLower(),
                                reloadedOrder.Id,
                                request.BasketId);

                            // Update the order entity with new PaymentIntentId
                            reloadedOrder.PaymentIntentId = idempotentPaymentIntent.PaymentIntentId;
                            _context.Orders.Update(reloadedOrder);
                            await _context.SaveChangesAsync();

                            // Also sync the basket in Redis with new PaymentIntentId
                            basket.PaymentIntentId = idempotentPaymentIntent.PaymentIntentId;
                            await _basketService.UpdateBasketAsync(basket);
                        }

                        var existingOrderDto = MapToOrderDto(reloadedOrder);
                        return existingOrderDto with { ClientSecret = idempotentPaymentIntent.ClientSecret };
                    }
                }
            }

            // PREVENTION OF DOUBLE ORDERS: Check if this basket is already being processed
            // 1. Check if basket has IsLocked flag
            if (basket.IsLocked)
            {
                // Edge case: Basket is locked but we couldn't find the associated pending order
                _logger.LogError(
                    "Edge case detected: Basket {BasketId} is locked but no Pending order found with PaymentIntentId {PaymentIntentId}. " +
                    "This may indicate incomplete order creation. Clearing lock.",
                    request.BasketId, basket.PaymentIntentId);

                // Clear the lock to allow retry
                basket.IsLocked = false;
                await _basketService.UpdateBasketAsync(basket);

                throw new DuneFlame.Domain.Exceptions.ConflictException(
                    "Order processing was interrupted. Please try again.");
            }

            // 2. Check if there's already a Pending order for this user within the last 60 seconds
            // This prevents double-click submissions where the same user tries to place the same order twice
            // Skip this check for guest users (userId is null)
            if (userId.HasValue)
            {
                var sixtySecondsAgo = DateTime.UtcNow.AddSeconds(-60);
                var recentPendingOrders = await _context.Orders
                    .Where(o => o.UserId == userId && 
                                o.Status == OrderStatus.Pending &&
                                o.CreatedAt > sixtySecondsAgo)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();

                if (recentPendingOrders.Any())
                {
                    _logger.LogWarning(
                        "Found {Count} recent pending orders for user {UserId} in the last 60 seconds. Preventing potential duplicate.",
                        recentPendingOrders.Count, userId);

                    // Check if any recent order has the same total amount (good indicator of duplicate attempt)
                    // We'll calculate the expected total first to make an accurate comparison
                    decimal expectedTotal = await CalculateOrderTotalAsync(userId, request, basket);

                    var potentialDuplicate = recentPendingOrders.FirstOrDefault(o => 
                        Math.Abs(o.TotalAmount - expectedTotal) < 0.01m); // Allow small floating point variance

                    if (potentialDuplicate != null)
                    {
                        _logger.LogWarning(
                            "Found exact duplicate order attempt for user {UserId}: OrderId={OrderId}, Amount={Amount}, CreatedAt={CreatedAt}",
                            userId, potentialDuplicate.Id, potentialDuplicate.TotalAmount, potentialDuplicate.CreatedAt);
                        throw new DuneFlame.Domain.Exceptions.ConflictException(
                            $"An identical order is already being processed (Order ID: {potentialDuplicate.Id}). Please wait at least 60 seconds before attempting again.");
                    }
                }
            }

            // Allow the order to dictate the final currency
            // The request.Currency takes precedence and will be used for the final order
            var requestCurrency = request.Currency;
            _logger.LogInformation(
                "Processing order with currency: {Currency} (basket was in {BasketCurrency})",
                requestCurrency, basket.CurrencyCode);

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
                PaymentIntentId = paymentIntentId,
                CurrencyCode = Enum.Parse<Currency>(requestCurrency, ignoreCase: true),
                LanguageCode = request.LanguageCode
            };

            decimal subtotal = 0;

            // Create order items from basket items
            // Price and weight MUST be taken from the database (ProductPrice), not from the basket (security)
            foreach (var basketItem in basket.Items)
            {
                // Get the product and its weight from the basket item's ProductPrice
                var originalProductPrice = await _context.ProductPrices
                    .Include(pp => pp.Product)
                    .ThenInclude(p => p.Translations)
                    .Include(pp => pp.Weight)
                    .FirstOrDefaultAsync(pp => pp.Id == basketItem.ProductPriceId) ?? throw new NotFoundException($"ProductPrice with ID {basketItem.ProductPriceId} not found");
                var product = originalProductPrice.Product ?? throw new NotFoundException($"Product for ProductPrice {basketItem.ProductPriceId} not found");

                // Get product name from translation
                var productName = product.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? "Unknown";

                // Now fetch the price for the requested currency using the same weight
                var requestedCurrencyEnum = Enum.Parse<Currency>(requestCurrency, ignoreCase: true);
                var requestedCurrencyPrice = await _context.ProductPrices
                    .Where(pp => pp.ProductId == product.Id && 
                                pp.ProductWeightId == originalProductPrice.ProductWeightId &&
                                pp.CurrencyCode == requestedCurrencyEnum)
                    .FirstOrDefaultAsync() ?? throw new NotFoundException($"Price not found for product '{productName}' in currency {requestCurrency}");

                // Calculate total weight in KG: Quantity * (WeightGrams / 1000)
                decimal totalWeightKg = basketItem.Quantity * (originalProductPrice.Weight!.Grams / 1000m);

                // Check stock availability based on weight
                if (product.StockInKg < totalWeightKg)
                {
                    throw new BadRequestException(
                        $"Insufficient stock for product '{productName}' (Weight: {originalProductPrice.Weight.Label}). " +
                        $"Available: {product.StockInKg}kg, Requested: {totalWeightKg}kg");
                }

                    // Create OrderItem (snapshot) with database price in requested currency
                    // CRITICAL SECURITY: Use REAL price from database, not frontend-provided price
                    _logger.LogInformation(
                        "Price lookup for ProductPriceId {ProductPriceId}: Database price = {Price} {Currency} (original was {OriginalCurrency})",
                        basketItem.ProductPriceId, requestedCurrencyPrice.Price, requestedCurrencyPrice.CurrencyCode, originalProductPrice.CurrencyCode);

                    var orderItem = new OrderItem
                    {
                        ProductPriceId = requestedCurrencyPrice.Id,
                        ProductName = productName,
                        UnitPrice = requestedCurrencyPrice.Price,
                        Quantity = basketItem.Quantity,
                        CurrencyCode = requestedCurrencyPrice.CurrencyCode
                    };

                    order.Items.Add(orderItem);
                    subtotal += requestedCurrencyPrice.Price * basketItem.Quantity;

                    // Decrement product stock by weight
                    product.StockInKg -= totalWeightKg;
                    _context.Products.Update(product);
                }

                // VALIDATION: Check if country code looks valid (should be ISO 2-letter code)
                if (request.ShippingAddress.Country.Length > 2)
                {
                    _logger.LogWarning(
                        "Shipping country code appears invalid for Order {OrderId}: '{Country}' (expected ISO 2-letter code). " +
                        "Frontend may be sending country name instead of ISO code.",
                        order.Id, request.ShippingAddress.Country);
                }

                // Calculate shipping cost based on destination country and currency
                var shippingCost = await _shippingService.GetShippingCostAsync(
                    request.ShippingAddress.Country,
                    order.CurrencyCode);

                // Calculate total: Subtotal + Shipping
                var totalAmount = subtotal + shippingCost;

                order.TotalAmount = totalAmount;

                _logger.LogInformation(
                    "Order total calculation: Subtotal={Subtotal}, Shipping={Shipping}, Total={Total} {Currency}",
                    subtotal, shippingCost, totalAmount, order.CurrencyCode);

            // Handle reward points redemption (only for registered users)
            if (userId.HasValue && request.UsePoints)
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
                    await _rewardService.RedeemPointsAsync(userId.Value, discount, order.Id);
                }
            }

            _context.Orders.Add(order);

            await _context.SaveChangesAsync();

            // Lock basket BEFORE creating PaymentIntent to prevent race conditions
            try
            {
                basket.IsLocked = true;
                await _basketService.UpdateBasketAsync(basket);
                _logger.LogInformation("Basket {BasketId} locked before payment processing for Order {OrderId}", 
                    request.BasketId, order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to lock basket {BasketId}. Continuing with payment processing.", 
                    request.BasketId);
            }

            // Create Stripe PaymentIntent for the order
            // Convert currency to lowercase (Stripe requirement: "usd", "aed")
            // NOTE: We pass BasketId so it can be stored in metadata for webhook to clean up basket after payment success
            var stripePaymentIntent = await _paymentService.CreatePaymentIntentAsync(
                order.TotalAmount,
                order.CurrencyCode.ToString().ToLower(),
                order.Id,
                request.BasketId);

            // Update order with PaymentIntentId from Stripe
            order.PaymentIntentId = stripePaymentIntent.PaymentIntentId;
            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Stripe PaymentIntent created for Order {OrderId}: PaymentIntentId={PaymentIntentId}, Amount={Amount} {Currency}",
                order.Id, stripePaymentIntent.PaymentIntentId, order.TotalAmount, order.CurrencyCode);

            // IMPORTANT: Save PaymentIntentId to basket in Redis for idempotency check on retry
            // This ensures that if user retries after canceling payment modal, idempotency check finds the existing order
            try
            {
                basket.PaymentIntentId = stripePaymentIntent.PaymentIntentId;
                await _basketService.UpdateBasketAsync(basket);
                _logger.LogInformation(
                    "Basket {BasketId} updated with PaymentIntentId {PaymentIntentId} for idempotency check",
                    request.BasketId, stripePaymentIntent.PaymentIntentId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Failed to update basket {BasketId} with PaymentIntentId. Idempotency check may not work on retry, but order is still created.",
                    request.BasketId);
                // Non-critical failure - order is already created
            }

            // NOTE: Basket cleanup is now handled in WebhookController.HandlePaymentIntentSucceeded
            // after payment is actually received (not just when order is created)

            await transaction.CommitAsync();

            _logger.LogInformation("Order created with ID {OrderId} for user {UserId}. Total: {TotalAmount}", 
                order.Id, userId, order.TotalAmount);

            // Reload order with related data for mapping
            var createdOrder = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.ApplicationUser)
                .FirstOrDefaultAsync(o => o.Id == order.Id);

            // Map to DTO and include ClientSecret (only for creation response)
            var orderDto = MapToOrderDto(createdOrder!);
            return orderDto with { ClientSecret = stripePaymentIntent.ClientSecret };
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
                    CurrencyCode = order.CurrencyCode,
                    Status = "Succeeded",
                    TransactionId = paymentIntentId,
                    PaymentMethod = "card"
                };

                _context.PaymentTransactions.Add(paymentTransaction);
                await _context.SaveChangesAsync();

                _logger.LogInformation("ProcessPaymentSuccess PHASE 3: Earn cashback points for Order {OrderId}",
                    order.Id);

                // PHASE 3: Earn Cashback Points (fresh scope, retryable, only for registered users)
                var cashback = RewardService.CalculateCashback(order.TotalAmount);
                order.PointsEarned = cashback;
                _context.Orders.Update(order);

                // Call RewardService to earn points only if user is registered
                if (order.UserId.HasValue)
                {
                    await _rewardService.EarnPointsAsync(order.UserId.Value, order.Id, cashback);
                }

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

        return [.. orders.Select(MapToOrderDto)];
    }

    public async Task<OrderDto> GetOrderByIdAsync(Guid id)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.ApplicationUser)
            .Include(o => o.PaymentTransactions)
            .FirstOrDefaultAsync(o => o.Id == id) ?? throw new NotFoundException($"Order with ID {id} not found");
        return MapToOrderDto(order);
    }

    /// <summary>
    /// Helper method to calculate expected order total for duplicate detection.
    /// This allows us to detect if a new order request has the same total as a recent pending order.
    /// </summary>
    private async Task<decimal> CalculateOrderTotalAsync(Guid? userId, CreateOrderRequest request, CustomerBasketDto basket)
    {
        try
        {
            decimal subtotal = 0;
            var requestCurrency = request.Currency;

            // Calculate subtotal from basket items
            foreach (var basketItem in basket.Items)
            {
                var originalProductPrice = await _context.ProductPrices
                    .Include(pp => pp.Product)
                    .Include(pp => pp.Weight)
                    .FirstOrDefaultAsync(pp => pp.Id == basketItem.ProductPriceId);

                if (originalProductPrice == null)
                    continue;

                // Fetch price in requested currency
                var requestedCurrencyEnum = Enum.Parse<Currency>(requestCurrency, ignoreCase: true);
                var requestedCurrencyPrice = await _context.ProductPrices
                    .Where(pp => pp.ProductId == originalProductPrice.ProductId && 
                                pp.ProductWeightId == originalProductPrice.ProductWeightId &&
                                pp.CurrencyCode == requestedCurrencyEnum)
                    .FirstOrDefaultAsync();

                if (requestedCurrencyPrice != null)
                {
                    subtotal += requestedCurrencyPrice.Price * basketItem.Quantity;
                }
            }

            // Get shipping cost
            var currency = Enum.Parse<Currency>(requestCurrency, ignoreCase: true);
            var shippingCost = await _shippingService.GetShippingCostAsync(
                request.ShippingAddress.Country,
                currency);

            var totalAmount = subtotal + shippingCost;

            // Handle reward points redemption if applicable
            if (request.UsePoints)
            {
                var wallet = await _context.RewardWallets
                    .FirstOrDefaultAsync(w => w.UserId == userId);

                if (wallet != null && wallet.Balance > 0)
                {
                    var discount = Math.Min(wallet.Balance, totalAmount);
                    totalAmount -= discount;
                }
            }

            return totalAmount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating expected order total for duplicate detection. Assuming unique order.");
            return -1; // Return negative value to skip duplicate amount comparison
        }
    }

    private static OrderDto MapToOrderDto(Order order)
    {
        var orderItems = order.Items.Select(oi => new OrderItemDto(
            oi.Id,
            oi.ProductPriceId,
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
            order.UserId,
            order.Status,
            order.TotalAmount,
            order.CurrencyCode,
            order.CreatedAt,
            shippingAddress,
            customerName,
            customerEmail,
            customerPhone,
            paymentTransactionId,
            order.PaymentIntentId,
            null, // ClientSecret is only included in CreateOrderResponse, not in OrderDto for GET requests
            orderItems
        );
    }
}
