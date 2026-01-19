using DuneFlame.Application.Interfaces;
using DuneFlame.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace DuneFlame.API.Controllers;

[Route("api/v1/webhooks")]
[ApiController]
public class WebhookController(
    IOrderService orderService,
    IBasketService basketService,
    IOptions<StripeSettings> stripeOptions,
    ILogger<WebhookController> logger) : ControllerBase
{
    private readonly IOrderService _orderService = orderService;
    private readonly IBasketService _basketService = basketService;
    private readonly StripeSettings _stripeSettings = stripeOptions.Value;
    private readonly ILogger<WebhookController> _logger = logger;

    /// <summary>
    /// Stripe webhook endpoint for handling payment events
    /// POST /api/v1/webhooks/stripe
    /// </summary>
    [HttpPost("stripe")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleStripeWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                _stripeSettings.WebhookSecret);

            _logger.LogInformation("Received Stripe event: {EventType} with ID: {EventId}",
                stripeEvent.Type, stripeEvent.Id);

            // Listen for payment_intent.succeeded event
            if (stripeEvent.Type == "payment_intent.succeeded")
            {
                var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                if (paymentIntent == null)
                {
                    _logger.LogWarning("Failed to deserialize payment intent from webhook");
                    return BadRequest("Invalid payment intent object");
                }

                _logger.LogInformation("Processing payment intent succeeded: {PaymentIntentId}",
                    paymentIntent.Id);

                // Call OrderService to process payment success
                await _orderService.ProcessPaymentSuccessAsync(paymentIntent.Id);

                // Delete basket from Redis if payment succeeded
                // Extract basketId from metadata if available
                if (paymentIntent.Metadata != null && paymentIntent.Metadata.TryGetValue("BasketId", out var basketId))
                {
                    _logger.LogInformation("Deleting basket {BasketId} after successful payment", basketId);
                    await _basketService.DeleteBasketAsync(basketId);
                    _logger.LogInformation("Redis basket {BasketId} deleted successfully", basketId);
                }

                _logger.LogInformation("Payment success processed for: {PaymentIntentId}", paymentIntent.Id);
            }
            else if (stripeEvent.Type == "payment_intent.payment_failed")
            {
                var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                if (paymentIntent != null)
                {
                    _logger.LogWarning("Payment failed for PaymentIntent {PaymentIntentId}", paymentIntent.Id);
                }
            }
            else
            {
                _logger.LogInformation("Unhandled event type: {EventType}", stripeEvent.Type);
            }

            return Ok(new { received = true });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe exception in webhook: {Message}", ex.Message);
            return BadRequest($"Invalid signature: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { error = "Webhook processing failed" });
        }
    }
}
