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

    public async Task<PaymentIntentResponse> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId)
    {
        try
        {
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

            // Convert amount to cents (Stripe works with smallest currency unit)
            var amountInCents = (long)(amount * 100);

            var options = new PaymentIntentCreateOptions
            {
                Amount = amountInCents,
                Currency = currency.ToLower(),
                Metadata = new Dictionary<string, string>
                {
                    { "OrderId", orderId.ToString() }
                }
            };

            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options);

            _logger.LogInformation(
                "Payment Intent created: {PaymentIntentId} for Order {OrderId}",
                paymentIntent.Id, orderId);

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

    public async Task<RefundResponse> RefundPaymentAsync(string transactionId, decimal amount)
    {
        try
        {
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

            // Convert amount to cents
            var amountInCents = (long)(amount * 100);

            var options = new RefundCreateOptions
            {
                PaymentIntent = transactionId,
                Amount = amountInCents
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

            // Convert amount to cents
            var amountInCents = (long)(amount * 100);

            string paymentIntentId;
            PaymentIntent paymentIntent;
            var service = new PaymentIntentService();

            // If PaymentIntentId exists, update it; otherwise create new
            if (!string.IsNullOrEmpty(basket.PaymentIntentId))
            {
                _logger.LogInformation("Updating existing PaymentIntent {PaymentIntentId} with amount {Amount}", 
                    basket.PaymentIntentId, amount);

                // Update existing PaymentIntent with amount and metadata
                var updateOptions = new PaymentIntentUpdateOptions
                {
                    Amount = amountInCents,
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
                _logger.LogInformation("Creating new PaymentIntent for basket {BasketId} with amount {Amount}", 
                    basketId, amount);

                // Create new PaymentIntent
                var createOptions = new PaymentIntentCreateOptions
                {
                    Amount = amountInCents,
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
                "PaymentIntent {PaymentIntentId} created/updated for basket {BasketId}",
                paymentIntentId, basketId);

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
