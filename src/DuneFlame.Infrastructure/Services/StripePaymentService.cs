using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace DuneFlame.Infrastructure.Services;

public class StripePaymentService(
    IOptions<StripeSettings> stripeOptions,
    AppDbContext context,
    ILogger<StripePaymentService> logger) : IPaymentService
{
    private readonly StripeSettings _stripeSettings = stripeOptions.Value;
    private readonly AppDbContext _context = context;
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
}
