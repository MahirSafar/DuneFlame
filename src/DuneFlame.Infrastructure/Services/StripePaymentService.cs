using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Services;

public class StripePaymentService(
    IOptions<StripeSettings> stripeOptions,
    AppDbContext context,
    IDistributedCache cache,
    ILogger<StripePaymentService> logger) : IPaymentService
{
    private readonly StripeSettings _stripeSettings = stripeOptions.Value;
    private readonly AppDbContext _context = context;
    private readonly IDistributedCache _cache = cache;
    private readonly ILogger<StripePaymentService> _logger = logger;

    /// <summary>
    /// Validates the ClientSecret format from Stripe PaymentIntent.
    /// Stripe client_secret format: pi_xxxxx_secret_yyyy (exactly one _secret_ part)
    /// </summary>
    private string ValidateAndReturnClientSecret(string? clientSecret, string paymentIntentId)
    {
        if (string.IsNullOrEmpty(clientSecret))
        {
            _logger.LogError(
                "PaymentIntent {PaymentIntentId} returned null or empty ClientSecret. " +
                "This is unexpected from Stripe API. Verify PaymentIntent object directly in Stripe dashboard.",
                paymentIntentId);
            throw new InvalidOperationException(
                $"PaymentIntent {paymentIntentId} returned invalid ClientSecret. " +
                "Expected format: pi_xxxxx_secret_yyyy. Received: empty or null.");
        }

        // Validate format: client_secret should contain exactly one "_secret_" part
        int secretCount = clientSecret.Split("_secret_").Length - 1;
        if (secretCount != 1)
        {
            _logger.LogError(
                "PaymentIntent {PaymentIntentId} returned malformed ClientSecret with {SecretCount} _secret_ parts. " +
                "ClientSecret: {ClientSecret}. Expected format: pi_xxxxx_secret_yyyy",
                paymentIntentId, secretCount, clientSecret);
            throw new InvalidOperationException(
                $"PaymentIntent {paymentIntentId} returned malformed ClientSecret. " +
                $"Expected exactly one '_secret_' part but found {secretCount}. " +
                "Verify PaymentIntent object directly in Stripe dashboard.");
        }

        _logger.LogDebug(
            "ClientSecret format validated successfully for PaymentIntent {PaymentIntentId}. " +
            "Format: pi_xxxxx_secret_yyyy",
            paymentIntentId);

        return clientSecret;
    }

    public async Task<PaymentIntentResponse> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId, string? basketId = null)
    {
        try
        {
            // SAFETY CHECK: Prevent zero or negative amounts from being sent to Stripe
            // These should be handled as zero-payment orders in OrderService
            if (amount <= 0)
            {
                _logger.LogError(
                    "Attempt to create PaymentIntent with invalid amount {Amount} for Order {OrderId}. " +
                    "Amount must be positive. Zero/negative amounts should be handled as zero-payment orders.",
                    amount, orderId);
                throw new InvalidOperationException(
                    $"Cannot create PaymentIntent with amount {amount}. Amount must be greater than 0. " +
                    "Zero or negative amounts should be marked as paid directly without Stripe processing.");
            }

            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

            // Convert amount to smallest currency unit (cents/fils) for Stripe
            // Stripe requires amounts as integers in the smallest currency unit
            long stripeAmount = (long)Math.Round(amount * 100, 0);

            var metadata = new Dictionary<string, string>
            {
                { "OrderId", orderId.ToString() }
            };

            // Add BasketId to metadata if provided (for webhook to clean up basket after payment success)
            if (!string.IsNullOrEmpty(basketId))
            {
                metadata.Add("BasketId", basketId);
            }

            var options = new PaymentIntentCreateOptions
            {
                Amount = stripeAmount,
                Currency = currency.ToLower(),
                Metadata = metadata,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions 
                { 
                    Enabled = true 
                }
            };

            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options);

            // Validate ClientSecret format before returning
            var validatedClientSecret = ValidateAndReturnClientSecret(paymentIntent.ClientSecret, paymentIntent.Id);

            _logger.LogInformation(
                "Payment Intent created: {PaymentIntentId} for Order {OrderId} with amount {Amount} {Currency}. " +
                "ClientSecret format validated successfully.",
                paymentIntent.Id, orderId, stripeAmount, currency);

            return new PaymentIntentResponse(validatedClientSecret, paymentIntent.Id, amount);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error creating payment intent for Order {OrderId}", orderId);
                throw new InvalidOperationException($"Failed to create payment intent: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating payment intent for Order {OrderId}", orderId);
                throw;
            }
        }

        public async Task<PaymentIntentResponse> GetPaymentIntentAsync(string paymentIntentId)
        {
            try
            {
                StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

                var service = new PaymentIntentService();
                var paymentIntent = await service.GetAsync(paymentIntentId);

                if (paymentIntent == null)
                {
                    _logger.LogWarning("PaymentIntent not found: {PaymentIntentId}", paymentIntentId);
                    throw new InvalidOperationException($"PaymentIntent {paymentIntentId} not found in Stripe.");
                }

                // Validate ClientSecret format before returning
                var validatedClientSecret = ValidateAndReturnClientSecret(paymentIntent.ClientSecret, paymentIntent.Id);

                _logger.LogInformation(
                    "Retrieved existing PaymentIntent: {PaymentIntentId} with ClientSecret format validated. " +
                    "ClientSecret format: pi_xxxxx_secret_yyyy",
                    paymentIntent.Id);

                // Convert Stripe amount from cents to decimal
                // In Stripe.NET SDK, Amount is nullable long
                long? stripeAmount = paymentIntent.Amount;
                decimal amount = stripeAmount.HasValue ? (stripeAmount.Value / 100m) : 0;
                return new PaymentIntentResponse(validatedClientSecret, paymentIntent.Id, amount);
            }
            catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
            {
                _logger.LogWarning(ex, "PaymentIntent {PaymentIntentId} not found in Stripe", paymentIntentId);
                throw new InvalidOperationException($"PaymentIntent {paymentIntentId} not found or expired.", ex);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe error retrieving payment intent: {PaymentIntentId}", paymentIntentId);
                throw new InvalidOperationException($"Failed to retrieve payment intent: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving payment intent: {PaymentIntentId}", paymentIntentId);
                throw;
            }
        }

        public async Task<RefundResponse> RefundPaymentAsync(string transactionId, decimal amount)
    {
        try
        {
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

            // Convert amount to smallest currency unit (cents/fils) for Stripe
            // Stripe requires amounts as integers in the smallest currency unit
            long stripeAmount = (long)Math.Round(amount * 100, 0);

            var options = new RefundCreateOptions
            {
                PaymentIntent = transactionId,
                Amount = stripeAmount
            };

            var service = new RefundService();
            var refund = await service.CreateAsync(options);

            _logger.LogInformation(
                "Refund created: {RefundId} for PaymentIntent {PaymentIntentId}. Amount: {Amount}",
                refund.Id, transactionId, amount);

            return new RefundResponse(refund.Id, amount, refund.Status);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error refunding payment: {TransactionId}", transactionId);
            throw new InvalidOperationException($"Failed to refund payment: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error refunding payment: {TransactionId}", transactionId);
            throw;
        }
    }

    public async Task<PaymentIntentResponse> CreateOrUpdatePaymentIntentAsync(string basketId, decimal amount, string currency = "usd")
    {
        try
        {
            // Get basket from cache
            var basketJson = await _cache.GetStringAsync(basketId);
            var basket = string.IsNullOrEmpty(basketJson)
                ? new DuneFlame.Application.DTOs.Basket.CustomerBasketDto { Id = basketId, Items = [] }
                : JsonSerializer.Deserialize<DuneFlame.Application.DTOs.Basket.CustomerBasketDto>(basketJson) 
                    ?? new DuneFlame.Application.DTOs.Basket.CustomerBasketDto { Id = basketId, Items = [] };

            // ZERO-PAYMENT / DUMMY ID CHECK: If basket already has an internal payment intent ID, return it immediately
            // without calling Stripe API. This handles orders where rewards covered the entire amount.
            if (!string.IsNullOrEmpty(basket.PaymentIntentId) && 
                (basket.PaymentIntentId == "internal_reward_payment" || 
                 basket.PaymentIntentId == "internal_minimum_threshold" ||
                 basket.PaymentIntentId.StartsWith("internal_")))
            {
                _logger.LogInformation(
                    "Detected internal payment intent ID {PaymentIntentId} for basket {BasketId}. " +
                    "Skipping Stripe API call. This order was already processed as zero-payment.",
                    basket.PaymentIntentId, basketId);

                // Return the dummy ID immediately without Stripe API calls
                return new PaymentIntentResponse(
                    ClientSecret: string.Empty,
                    PaymentIntentId: basket.PaymentIntentId,
                    Amount: 0);
            }

            // ZERO-AMOUNT CHECK: If amount is 0 or negative, don't call Stripe
            if (amount <= 0)
            {
                _logger.LogWarning(
                    "Attempted to create/update PaymentIntent for basket {BasketId} with zero or negative amount {Amount}. " +
                    "Skipping Stripe API call. Amount should have been handled as zero-payment during order creation.",
                    basketId, amount);

                // Return a response indicating payment is not required
                return new PaymentIntentResponse(
                    ClientSecret: string.Empty,
                    PaymentIntentId: "internal_zero_amount",
                    Amount: 0);
            }

            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

            // Convert amount to smallest currency unit (cents/fils) for Stripe
            // Stripe requires amounts as integers in the smallest currency unit
            long stripeAmount = (long)Math.Round(amount * 100, 0);

            string paymentIntentId;
            PaymentIntent paymentIntent;
            var service = new PaymentIntentService();

            // If PaymentIntentId exists, validate it and update if still valid
            if (!string.IsNullOrEmpty(basket.PaymentIntentId))
            {
                _logger.LogInformation("Validating existing PaymentIntent {PaymentIntentId} for basket {BasketId}", 
                    basket.PaymentIntentId, basketId);

                try
                {
                    // Try to retrieve the PaymentIntent to validate it still exists and is valid
                    var existingIntent = await service.GetAsync(basket.PaymentIntentId);

                    if (existingIntent != null)
                    {
                        _logger.LogInformation("PaymentIntent {PaymentIntentId} is valid. Updating with amount {Amount} {Currency}", 
                            basket.PaymentIntentId, stripeAmount, currency);

                        // Update existing PaymentIntent with amount and metadata
                        var updateOptions = new PaymentIntentUpdateOptions
                        {
                            Amount = stripeAmount,
                            Metadata = new Dictionary<string, string>
                            {
                                { "BasketId", basketId }
                            }
                        };
                        paymentIntent = await service.UpdateAsync(basket.PaymentIntentId, updateOptions);
                        paymentIntentId = paymentIntent.Id;
                    }
                    else
                    {
                        // PaymentIntent retrieval returned null - create a new one
                        _logger.LogWarning("PaymentIntent {PaymentIntentId} returned null. Creating new one for basket {BasketId}", 
                            basket.PaymentIntentId, basketId);

                        throw new InvalidOperationException("PaymentIntent is no longer available");
                    }
                }
                catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
                {
                    // PaymentIntent not found in Stripe (expired or deleted) - create a new one
                    _logger.LogWarning(ex, "PaymentIntent {PaymentIntentId} not found in Stripe (expired/deleted). Creating new one for basket {BasketId}", 
                        basket.PaymentIntentId, basketId);

                    var createOptions = new PaymentIntentCreateOptions
                    {
                        Amount = stripeAmount,
                        Currency = currency.ToLower(),
                        Metadata = new Dictionary<string, string>
                        {
                            { "BasketId", basketId }
                        },
                        AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions 
                        { 
                            Enabled = true 
                        }
                    };
                    paymentIntent = await service.CreateAsync(createOptions);
                    paymentIntentId = paymentIntent.Id;
                }
                catch (Exception ex)
                {
                    // Other errors during validation - log and create a new one
                    _logger.LogWarning(ex, "Error validating PaymentIntent {PaymentIntentId} for basket {BasketId}. Creating new one", 
                        basket.PaymentIntentId, basketId);

                    var createOptions = new PaymentIntentCreateOptions
                    {
                        Amount = stripeAmount,
                        Currency = currency.ToLower(),
                        Metadata = new Dictionary<string, string>
                        {
                            { "BasketId", basketId }
                        },
                        AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions 
                        { 
                            Enabled = true 
                        }
                    };
                    paymentIntent = await service.CreateAsync(createOptions);
                    paymentIntentId = paymentIntent.Id;
                }
            }
            else
            {
                _logger.LogInformation("No existing PaymentIntent for basket {BasketId}. Creating new one with amount {Amount} {Currency}", 
                    basketId, stripeAmount, currency);

                // Create new PaymentIntent
                var createOptions = new PaymentIntentCreateOptions
                {
                    Amount = stripeAmount,
                    Currency = currency.ToLower(),
                    Metadata = new Dictionary<string, string>
                    {
                        { "BasketId", basketId }
                    },
                    AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions 
                    { 
                        Enabled = true 
                    }
                };
                paymentIntent = await service.CreateAsync(createOptions);
                paymentIntentId = paymentIntent.Id;
            }

            // Update basket with PaymentIntent details
            basket.PaymentIntentId = paymentIntentId;

            // Validate ClientSecret format before storing and returning
            var validatedClientSecret = ValidateAndReturnClientSecret(paymentIntent.ClientSecret, paymentIntentId);
            basket.ClientSecret = validatedClientSecret;

            // Persist basket back to cache
            var updatedBasketJson = JsonSerializer.Serialize(basket);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.AddDays(30)
            };
            await _cache.SetStringAsync(basketId, updatedBasketJson, cacheOptions);

            _logger.LogInformation(
                "PaymentIntent {PaymentIntentId} created/updated for basket {BasketId} with amount {Amount}. " +
                "ClientSecret format validated successfully.",
                paymentIntentId, basketId, stripeAmount);

            return new PaymentIntentResponse(validatedClientSecret, paymentIntentId, amount);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error in CreateOrUpdatePaymentIntent for basket {BasketId}", basketId);
            throw new InvalidOperationException($"Failed to create or update payment intent: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in CreateOrUpdatePaymentIntent for basket {BasketId}", basketId);
            throw;
        }
    }

    /// <summary>
    /// Create a Checkout Session for a product with dynamic Stripe Price ID
    /// Looks up the Price ID from Products dictionary using ItemCode
    /// </summary>
    public async Task<string> CreateCheckoutSessionAsync(
        string itemCode,
        decimal quantity,
        Guid orderId,
        string? basketId = null,
        string successUrl = "",
        string cancelUrl = "")
    {
        try
        {
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

            // Validate and get Price ID from Products dictionary
            if (string.IsNullOrEmpty(itemCode))
            {
                _logger.LogWarning("ItemCode is empty for Order {OrderId}", orderId);
                throw new InvalidOperationException("ItemCode cannot be empty.");
            }

            if (!_stripeSettings.Products.TryGetValue(itemCode, out var priceId))
            {
                _logger.LogWarning("Price ID not found for ItemCode {ItemCode} for Order {OrderId}", itemCode, orderId);
                throw new InvalidOperationException($"Price ID not found for product code: {itemCode}");
            }

            if (string.IsNullOrEmpty(priceId))
            {
                _logger.LogWarning("Price ID is empty for ItemCode {ItemCode} for Order {OrderId}", itemCode, orderId);
                throw new InvalidOperationException($"Price ID is empty for product code: {itemCode}");
            }

            var metadata = new Dictionary<string, string>
            {
                { "OrderId", orderId.ToString() },
                { "ItemCode", itemCode }
            };

            if (!string.IsNullOrEmpty(basketId))
            {
                metadata.Add("BasketId", basketId);
            }

            var lineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = (long)quantity
                }
            };

            var options = new SessionCreateOptions
            {
                LineItems = lineItems,
                Mode = "payment",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Metadata = metadata,
                AutomaticTax = new SessionAutomaticTaxOptions { Enabled = false }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            _logger.LogInformation(
                "Checkout Session created: {SessionId} for Order {OrderId} with ItemCode {ItemCode} (PriceId {PriceId}), Quantity {Quantity}",
                session.Id, orderId, itemCode, priceId, quantity);

            return session.Id;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error creating checkout session for Order {OrderId} with ItemCode {ItemCode}", orderId, itemCode);
            throw new InvalidOperationException($"Failed to create checkout session: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating checkout session for Order {OrderId}", orderId);
            throw;
        }
    }
}
