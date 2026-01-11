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
    IOptions<StripeSettings> stripeOptions,
    ILogger<WebhookController> logger) : ControllerBase
{
    private readonly IOrderService _orderService = orderService;
    private readonly StripeSettings _stripeSettings = stripeOptions.Value;
    private readonly ILogger<WebhookController> _logger = logger;

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

                _logger.LogInformation("Payment success processed for: {PaymentIntentId}", paymentIntent.Id);
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
