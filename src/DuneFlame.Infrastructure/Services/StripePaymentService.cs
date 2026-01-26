using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
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

    public async Task<PaymentIntentResponse> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId, string? basketId = null)
    {
        try
        {
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
                Metadata = metadata
            };

            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options);

            _logger.LogInformation(
                "Payment Intent created: {PaymentIntentId} for Order {OrderId} with amount {Amount} {Currency}",
                paymentIntent.Id, orderId, stripeAmount, currency);

            return new PaymentIntentResponse(paymentIntent.ClientSecret, paymentIntent.Id);
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

                _logger.LogInformation(
                    "Retrieved existing PaymentIntent: {PaymentIntentId} with ClientSecret {ClientSecret}",
                    paymentIntent.Id, paymentIntent.ClientSecret ?? "N/A");

                return new PaymentIntentResponse(paymentIntent.ClientSecret ?? string.Empty, paymentIntent.Id);
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
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

            // Get basket from cache
            var basketJson = await _cache.GetStringAsync(basketId);
            var basket = string.IsNullOrEmpty(basketJson)
                ? new DuneFlame.Application.DTOs.Basket.CustomerBasketDto { Id = basketId, Items = [] }
                : JsonSerializer.Deserialize<DuneFlame.Application.DTOs.Basket.CustomerBasketDto>(basketJson) 
                    ?? new DuneFlame.Application.DTOs.Basket.CustomerBasketDto { Id = basketId, Items = [] };

            // Convert amount to smallest currency unit (cents/fils) for Stripe
            // Stripe requires amounts as integers in the smallest currency unit
            long stripeAmount = (long)Math.Round(amount * 100, 0);

            string paymentIntentId;
            PaymentIntent paymentIntent;
            var service = new PaymentIntentService();

            // If PaymentIntentId exists, update it; otherwise create new
            if (!string.IsNullOrEmpty(basket.PaymentIntentId))
            {
                _logger.LogInformation("Updating existing PaymentIntent {PaymentIntentId} with amount {Amount} {Currency}", 
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
                _logger.LogInformation("Creating new PaymentIntent for basket {BasketId} with amount {Amount} {Currency}", 
                    basketId, stripeAmount, currency);

                // Create new PaymentIntent
                var createOptions = new PaymentIntentCreateOptions
                {
                    Amount = stripeAmount,
                    Currency = currency.ToLower(),
                    Metadata = new Dictionary<string, string>
                    {
                        { "BasketId", basketId }
                    }
                };
                paymentIntent = await service.CreateAsync(createOptions);
                paymentIntentId = paymentIntent.Id;
            }

            // Update basket with PaymentIntent details
            basket.PaymentIntentId = paymentIntentId;
            basket.ClientSecret = paymentIntent.ClientSecret;

            // Persist basket back to cache
            var updatedBasketJson = JsonSerializer.Serialize(basket);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.AddDays(30)
            };
            await _cache.SetStringAsync(basketId, updatedBasketJson, cacheOptions);

            _logger.LogInformation(
                "PaymentIntent {PaymentIntentId} created/updated for basket {BasketId} with amount {Amount}",
                paymentIntentId, basketId, stripeAmount);

            return new PaymentIntentResponse(paymentIntent.ClientSecret, paymentIntentId);
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
}
