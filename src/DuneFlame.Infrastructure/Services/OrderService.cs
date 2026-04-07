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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim> _basketLocks = new();

    public async Task<OrderDto> CreateOrderAsync(Guid? userId, CreateOrderRequest request)
    {
        // FAIL-FAST GATEKEEPER: Ensure only ONE thread processes a basket at a time
        var semaphore = _basketLocks.GetOrAdd(request.BasketId, _ => new System.Threading.SemaphoreSlim(1, 1));

        // Wait up to 2 seconds for the lock. If it's locked longer, another thread is processing it.
        if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            _logger.LogWarning("Concurrent checkout request blocked at the gate for BasketId: {BasketId}", request.BasketId);
            // Return a clean 409 Conflict to the frontend
            throw new DuneFlame.Domain.Exceptions.ConflictException("Your order is currently being processed. Please wait a moment.");
        }

        try
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

                // IDEMPOTENCY CHECK: Look up the existing 'Pending' order by basketId
                // This handles the scenario where user cancels modal, changes cart, and clicks "Proceed" again
                var existingOrder = await _context.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o =>
                        o.BasketId == request.BasketId &&
                        o.Status == OrderStatus.Pending);

                bool isIdempotentRetry = existingOrder != null;

                if (existingOrder != null)
                {
                    _logger.LogInformation("Idempotent path: Found existing Pending order {OrderId}. Performing Hard-Delete to ensure a clean slate.", existingOrder.Id);

                    if (existingOrder.PointsRedeemed > 0 && existingOrder.UserId.HasValue)
                    {
                        await _rewardService.RefundPointsAsync(existingOrder.UserId.Value, existingOrder.Id, existingOrder.PointsEarned, existingOrder.PointsRedeemed);
                    }

                    // Eject the old order completely to avoid all EF Core tracking conflicts!
                    _context.OrderItems.RemoveRange(existingOrder.Items);
                    _context.Orders.Remove(existingOrder);
                    await _context.SaveChangesAsync(); // Commit the delete immediately

                    existingOrder = null; // Reset so the rest of the method creates a new order
                }

                // PREVENTION OF DOUBLE ORDERS: Check if this basket is already being processed
                // 1. Check if basket has IsLocked flag
                if (basket.IsLocked && !isIdempotentRetry)
                {
                    // Edge case: Basket is locked but we couldn't find the associated pending order
                    _logger.LogError(
                        "Edge case detected: Basket {BasketId} is locked but no Pending order found with PaymentIntentId {PaymentIntentId}. " +
                        "This may indicate incomplete order creation. Clearing lock.",
                        request.BasketId, basket.PaymentIntentId);

                    // Clear the lock to allow retry
                    basket.IsLocked = false;
                    await _basketService.UpdateBasketAsync(basket, false);

                    throw new DuneFlame.Domain.Exceptions.ConflictException(
                        "Order processing was interrupted. Please try again.");
                }

                // 2. Check if there's already a Pending order for this user/basket within the last 60 seconds
                if (!isIdempotentRetry)
                {
                    var sixtySecondsAgo = DateTime.UtcNow.AddSeconds(-60);
                    var recentPendingOrders = await _context.Orders
                        .Where(o => o.BasketId == request.BasketId &&
                                    o.Status == OrderStatus.Pending &&
                                    o.CreatedAt > sixtySecondsAgo)
                        .OrderByDescending(o => o.CreatedAt)
                        .ToListAsync();

                    if (recentPendingOrders.Any())
                    {
                        _logger.LogWarning(
                            "Found {Count} recent pending orders for basket {BasketId} in the last 60 seconds. Preventing potential duplicate.",
                            recentPendingOrders.Count, request.BasketId);

                        // Check if any recent order has the same total amount (good indicator of duplicate attempt)
                        // We'll calculate the expected total first to make an accurate comparison
                        decimal expectedTotal = await CalculateOrderTotalAsync(userId, request, basket);

                        var potentialDuplicate = recentPendingOrders.FirstOrDefault(o =>
                            Math.Abs(o.TotalAmount - expectedTotal) < 0.01m); // Allow small floating point variance

                        if (potentialDuplicate != null)
                        {
                            _logger.LogWarning(
                                "Found exact duplicate order attempt for basket {BasketId}: OrderId={OrderId}, Amount={Amount}, CreatedAt={CreatedAt}",
                                request.BasketId, potentialDuplicate.Id, potentialDuplicate.TotalAmount, potentialDuplicate.CreatedAt);
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
                    BasketId = request.BasketId,
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
                var newOrderItems = new List<OrderItem>();

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

                    newOrderItems.Add(orderItem);
                    subtotal += requestedCurrencyPrice.Price * basketItem.Quantity;
                }

                decimal originalSubtotalForShipping = subtotal;

                // WELCOME DISCOUNT LOGIC for first time buyers
                decimal welcomeDiscount = 0;
                if (userId.HasValue)
                {
                    bool hasPreviousOrders = await _context.Orders.AnyAsync(o => o.UserId == userId.Value && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Pending);
                    if (!hasPreviousOrders)
                    {
                        welcomeDiscount = Math.Round(subtotal * 0.10m, 2);
                        subtotal -= welcomeDiscount;

                        _logger.LogInformation("Applied 10% welcome discount of {DiscountAmount} for user {UserId}", welcomeDiscount, userId.Value);
                    }
                }

                // VALIDATION: Check if country code looks valid (should be ISO 2-letter code)
                if (request.ShippingAddress.Country.Length > 2)
                {
                    _logger.LogWarning(
                        "Shipping country code appears invalid for Order {OrderId}: '{Country}' (expected ISO 2-letter code). " +
                        "Frontend may be sending country name instead of ISO code.",
                        order.Id, request.ShippingAddress.Country);
                }

                // Calculate shipping cost based on destination country, currency, and subtotal (with promotion logic)
                var shippingCost = await _shippingService.GetShippingCostWithPromotionAsync(
                    request.ShippingAddress.Country,
                    order.CurrencyCode,
                    originalSubtotalForShipping);

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
                        await _rewardService.RedeemPointsAsync(userId.Value, discount, order.Id, false);
                    }
                }

                // Ensure final amount is never negative (cap at 0)
                if (order.TotalAmount < 0)
                {
                    order.TotalAmount = 0;
                }

                // Lock basket BEFORE creating PaymentIntent to prevent race conditions
                try
                {
                    basket.IsLocked = true;
                    await _basketService.UpdateBasketAsync(basket, false);
                    _logger.LogInformation("Basket {BasketId} locked before payment processing for Order {OrderId}",
                        request.BasketId, order.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to lock basket {BasketId}. Continuing with payment processing.",
                        request.BasketId);
                }

                // If basket already has a valid Stripe PaymentIntent (Express Checkout flow), reuse it
                // to avoid creating a duplicate PI that would never match the one the user already paid.
                // Otherwise, create a new PaymentIntent (traditional checkout flow).
                PaymentIntentResponse stripePaymentIntent;
                // Only interact with Stripe if there is a positive total amount
                if (order.TotalAmount > 0)
                {
                    if (!string.IsNullOrEmpty(order.PaymentIntentId) && order.PaymentIntentId.StartsWith("pi_"))
                    {
                        _logger.LogInformation(
                            "Reusing and updating existing PaymentIntent {PaymentIntentId} for Order {OrderId}",
                            order.PaymentIntentId, order.Id);

                        // Ensure the Stripe intent holds the updated amount in case cart has changed
                        stripePaymentIntent = await _paymentService.CreateOrUpdatePaymentIntentAsync(
                            request.BasketId,
                            order.TotalAmount,
                            order.CurrencyCode.ToString().ToLower());

                        // CRITICAL FIX: Assign the potentially newly generated Intent ID back to the order!
                        order.PaymentIntentId = stripePaymentIntent.PaymentIntentId;
                    }
                    else
                    {
                        // Create Stripe PaymentIntent for the order
                        // Convert currency to lowercase (Stripe requirement: "usd", "aed")
                        // NOTE: We pass BasketId so it can be stored in metadata for webhook to clean up basket after payment success
                        stripePaymentIntent = await _paymentService.CreatePaymentIntentAsync(
                            order.TotalAmount,
                            order.CurrencyCode.ToString().ToLower(),
                            order.Id,
                            request.BasketId);

                        // Update order with PaymentIntentId from Stripe
                        order.PaymentIntentId = stripePaymentIntent.PaymentIntentId;

                        _logger.LogInformation(
                            "Stripe PaymentIntent created for Order {OrderId}: PaymentIntentId={PaymentIntentId}, Amount={Amount} {Currency}",
                            order.Id, stripePaymentIntent.PaymentIntentId, order.TotalAmount, order.CurrencyCode);

                        // IMPORTANT: Save PaymentIntentId to basket in Redis for idempotency check on retry
                        // This ensures that if user retries after canceling payment modal, idempotency check finds the existing order
                        try
                        {
                            basket.PaymentIntentId = stripePaymentIntent.PaymentIntentId;
                            await _basketService.UpdateBasketAsync(basket, false);
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
                    }
                }
                else
                {
                    stripePaymentIntent = new PaymentIntentResponse(string.Empty, "internal_reward_payment");
                }

                foreach (var item in newOrderItems)
                {
                    order.Items.Add(item);
                }
                _context.Orders.Add(order);

                // ZERO-PAYMENT LOGIC: If final amount is 0, bypass Stripe and mark order as Paid
                if (order.TotalAmount == 0)
                {
                    _logger.LogInformation(
                        "Zero-payment order detected for Order {OrderId}. Reward covered the entire order. Bypassing Stripe.",
                        order.Id);

                    await DeductStockForOrderAsync(newOrderItems);

                    // Mark order as Paid immediately
                    order.Status = OrderStatus.Paid;

                    // Set a dummy PaymentIntentId for internal tracking
                    const string DummyPaymentIntentId = "internal_reward_payment";
                    order.PaymentIntentId = DummyPaymentIntentId;

                    // Record payment transaction for internal tracking
                    var paymentTransaction = new PaymentTransaction
                    {
                        OrderId = order.Id,
                        Amount = order.TotalAmount,
                        CurrencyCode = order.CurrencyCode,
                        Status = "Succeeded",
                        TransactionId = DummyPaymentIntentId,
                        PaymentMethod = "internal_reward"
                    };

                    _context.PaymentTransactions.Add(paymentTransaction);

                    decimal cashback = 0;
                    if (userId.HasValue)
                    {
                        cashback = RewardService.CalculateCashback(order.TotalAmount);
                        order.PointsEarned = cashback;
                    }

                    try
                    {
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                    catch (DbUpdateConcurrencyException ex)
                    {
                        _logger.LogWarning(ex, "Concurrency conflict during order creation for basket {BasketId}", request.BasketId);

                        var lockedBasket = await _basketService.GetBasketAsync(request.BasketId);
                        if (lockedBasket != null && lockedBasket.IsLocked)
                        {
                            lockedBasket.IsLocked = false;
                            await _basketService.UpdateBasketAsync(lockedBasket, false);
                        }

                        throw new DuneFlame.Domain.Exceptions.ConflictException("Order processing was interrupted by a concurrent update. Please try again.");
                    }

                    // Earn cashback points for registered users even on zero-payment orders
                    if (userId.HasValue)
                    {
                        await _rewardService.EarnPointsAsync(userId.Value, order.Id, cashback, false);
                    }

                    // Update basket with dummy PaymentIntentId for consistency
                    try
                    {
                        basket.PaymentIntentId = DummyPaymentIntentId;
                        await _basketService.UpdateBasketAsync(basket, false);
                        _logger.LogInformation(
                            "Basket {BasketId} updated with dummy PaymentIntentId for zero-payment order {OrderId}",
                            request.BasketId, order.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to update basket {BasketId} with dummy PaymentIntentId.",
                            request.BasketId);
                    }

                    _logger.LogInformation(
                        "Zero-payment order {OrderId} completed successfully. No Stripe charge required. User rewarded for redemption.",
                        order.Id);

                    // Reload order with related data for mapping
                    var zeroPaymentOrder = await _context.Orders
                        .Include(o => o.Items)
                        .Include(o => o.ApplicationUser)
                        .FirstOrDefaultAsync(o => o.Id == order.Id);

                    var zeroPaymentOrderDto = MapToOrderDto(zeroPaymentOrder!);
                    // Return success response with empty client secret (no payment required)
                    return zeroPaymentOrderDto with { ClientSecret = string.Empty };
                }

                // STRIPE MINIMUM CHECK: If amount is positive but less than Stripe minimum, treat as free order
                // Stripe minimum charge varies by currency, but typically $0.50 USD / 50 fils AED
                const decimal StripeMinimumUsd = 0.50m;
                const decimal StripeMinimumAed = 1.83m; // ~50 fils in AED

                decimal stripeMinimum = order.CurrencyCode switch
                {
                    Currency.AED => StripeMinimumAed,
                    _ => StripeMinimumUsd // Default to USD minimum
                };

                if (order.TotalAmount > 0 && order.TotalAmount < stripeMinimum)
                {
                    _logger.LogInformation(
                        "Order {OrderId} amount {Amount} {Currency} is below Stripe minimum {Minimum}. Treating as zero-payment order.",
                        order.Id, order.TotalAmount, order.CurrencyCode, stripeMinimum);

                    await DeductStockForOrderAsync(newOrderItems);

                    // Treat as free order (same logic as zero-payment)
                    order.Status = OrderStatus.Paid;
                    const string DummyPaymentIntentId = "internal_minimum_threshold";
                    order.PaymentIntentId = DummyPaymentIntentId;

                    var paymentTransaction = new PaymentTransaction
                    {
                        OrderId = order.Id,
                        Amount = order.TotalAmount,
                        CurrencyCode = order.CurrencyCode,
                        Status = "Succeeded",
                        TransactionId = DummyPaymentIntentId,
                        PaymentMethod = "internal_minimum_threshold"
                    };

                    _context.PaymentTransactions.Add(paymentTransaction);

                    if (userId.HasValue)
                    {
                        var cashback = RewardService.CalculateCashback(order.TotalAmount);
                        order.PointsEarned = cashback;
                        await _rewardService.EarnPointsAsync(userId.Value, order.Id, cashback, false);
                    }

                    try
                    {
                        basket.PaymentIntentId = DummyPaymentIntentId;
                        await _basketService.UpdateBasketAsync(basket, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update basket {BasketId} with minimum threshold PaymentIntentId.", request.BasketId);
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation(
                        "Minimum threshold order {OrderId} completed successfully. Amount {Amount} {Currency} below minimum charge.",
                        order.Id, order.TotalAmount, order.CurrencyCode);

                    var minimumThresholdOrder = await _context.Orders
                        .Include(o => o.Items)
                        .Include(o => o.ApplicationUser)
                        .FirstOrDefaultAsync(o => o.Id == order.Id);

                    var minimumThresholdOrderDto = MapToOrderDto(minimumThresholdOrder!);
                    return minimumThresholdOrderDto with { ClientSecret = string.Empty };
                }

                try
                {
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    _logger.LogWarning(ex, "Concurrency conflict during order creation for basket {BasketId}", request.BasketId);

                    var lockedBasket = await _basketService.GetBasketAsync(request.BasketId);
                    if (lockedBasket != null && lockedBasket.IsLocked)
                    {
                        lockedBasket.IsLocked = false;
                        await _basketService.UpdateBasketAsync(lockedBasket, false);
                    }

                    throw new DuneFlame.Domain.Exceptions.ConflictException("Order processing was interrupted by a concurrent update. Please try again.");
                }

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

                // CRITICAL: Release Redis lock if EF transaction fails
                try
                {
                    var lockedBasket = await _basketService.GetBasketAsync(request.BasketId);
                    if (lockedBasket != null && lockedBasket.IsLocked)
                    {
                        lockedBasket.IsLocked = false;
                        await _basketService.UpdateBasketAsync(lockedBasket, false);
                    }
                }
                catch { /* Suppress */ }

                _logger.LogError(ex, "Error creating order for user {UserId}", userId);
                throw;
            }
        }
        finally
        {
            // ALWAYS release the memory lock so the user can try again
            semaphore.Release();
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
                    .Include(o => o.Items)
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

                // Update status atomically and deduct stock
                order.Status = OrderStatus.Paid;
                await DeductStockForOrderAsync(order.Items);
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
                    await _rewardService.EarnPointsAsync(order.UserId.Value, order.Id, cashback, false);
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
                _context.ChangeTracker.Clear();
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
                _context.ChangeTracker.Clear();
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

            decimal originalSubtotalForShipping = subtotal;

            // WELCOME DISCOUNT LOGIC for first time buyers in expected total
            decimal welcomeDiscount = 0;
            if (userId.HasValue)
            {
                bool hasPreviousOrders = await _context.Orders.AnyAsync(o => o.UserId == userId.Value && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Pending);
                if (!hasPreviousOrders)
                {
                    welcomeDiscount = Math.Round(subtotal * 0.10m, 2);
                    subtotal -= welcomeDiscount;
                }
            }

            // Get shipping cost with promotion logic
            var currency = Enum.Parse<Currency>(requestCurrency, ignoreCase: true);
            var shippingCost = await _shippingService.GetShippingCostWithPromotionAsync(
                request.ShippingAddress.Country,
                currency,
                originalSubtotalForShipping);

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

            // Ensure total is never negative (cap at 0)
            if (totalAmount < 0)
            {
                totalAmount = 0;
            }

            return totalAmount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating expected order total for duplicate detection. Assuming unique order.");
            return -1; // Return negative value to skip duplicate amount comparison
        }
    }

    private async Task DeductStockForOrderAsync(IEnumerable<OrderItem> items)
    {
        foreach (var item in items)
        {
            var productPrice = await _context.ProductPrices.Include(pp => pp.Weight).FirstOrDefaultAsync(pp => pp.Id == item.ProductPriceId);
            if (productPrice != null && productPrice.Weight != null)
            {
                decimal totalWeightKg = item.Quantity * (productPrice.Weight.Grams / 1000m);
                // ATOMIC UPDATE: Prevents concurrent requests from overwriting each other's stock reduction
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE Products SET StockInKg = StockInKg - {totalWeightKg} WHERE Id = {productPrice.ProductId} AND StockInKg >= {totalWeightKg}");
            }
        }
    }

    public async Task CancelAbandonedOrderAsync(Guid orderId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null) return;

        // 1. HARD DELETE the order to keep the database clean
        _context.Orders.Remove(order);

        // 2. Unlock the basket
        if (!string.IsNullOrEmpty(order.BasketId))
        {
            var basket = await _basketService.GetBasketAsync(order.BasketId);
            if (basket != null)
            {
                basket.PaymentIntentId = null;
                basket.IsLocked = false;
                await _basketService.UpdateBasketAsync(basket, false);
            }
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Abandoned Order {OrderId} was hard-deleted to keep database clean.", orderId);
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
