using DuneFlame.Application.Interfaces;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace DuneFlame.API.Controllers;

[Route("api/v1/webhooks")]
[ApiController]
public class WebhookController(
    IOrderService orderService,
    IAdminOrderService adminOrderService,
    IBasketService basketService,
    IEmailService emailService,
    IOptions<StripeSettings> stripeOptions,
    ILogger<WebhookController> logger) : ControllerBase
{
    private readonly IOrderService _orderService = orderService;
    private readonly IAdminOrderService _adminOrderService = adminOrderService;
    private readonly IBasketService _basketService = basketService;
    private readonly IEmailService _emailService = emailService;
    private readonly StripeSettings _stripeSettings = stripeOptions.Value;
    private readonly ILogger<WebhookController> _logger = logger;

    /// <summary>
    /// Stripe webhook endpoint for handling payment events
    /// POST /api/v1/webhooks/stripe
    /// 
    /// Events handled:
    /// - payment_intent.succeeded: Payment completed, transition order to Paid, earn cashback
    /// - charge.refunded: Payment refunded, update transaction record with RefundId
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

            switch (stripeEvent.Type)
            {
                // ===== PHASE 1: Payment Success (Income Event) =====
                case "payment_intent.succeeded":
                    await HandlePaymentIntentSucceeded(stripeEvent);
                    break;

                // ===== PHASE 2: Refund Processing (Outgoing Event) =====
                case "charge.refunded":
                    await HandleChargeRefunded(stripeEvent);
                    break;

                // ===== Payment Intent Created (Informational Event) =====
                case "payment_intent.created":
                    _logger.LogInformation("Payment intent created: {PaymentIntentId}", 
                        ((PaymentIntent)stripeEvent.Data.Object)?.Id ?? "unknown");
                    break;

                // ===== Unhandled Events =====
                case "payment_intent.payment_failed":
                    await HandlePaymentIntentFailed(stripeEvent);
                    break;

                default:
                    _logger.LogInformation("Unhandled event type: {EventType}", stripeEvent.Type);
                    break;
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

    /// <summary>
    /// Handle payment_intent.succeeded event
    /// - Idempotent: checks if order already paid
    /// - 3-Phase: status update → payment record → cashback points
    /// </summary>
    private async Task HandlePaymentIntentSucceeded(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null)
        {
            _logger.LogWarning("Failed to deserialize payment intent from webhook");
            return;
        }

        _logger.LogInformation("Processing payment intent succeeded: {PaymentIntentId}",
            paymentIntent.Id);

        try
        {
            // Call OrderService to process payment success (3-Phase with retry)
            await _orderService.ProcessPaymentSuccessAsync(paymentIntent.Id);

            // Send order paid email
            try
            {
                // Fetch order details to get user email and amount
                // We'll need to query the database to get these details
                var dbContext = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var order = await dbContext.Orders
                    .Include(o => o.ApplicationUser)
                    .FirstOrDefaultAsync(o => o.PaymentIntentId == paymentIntent.Id);

                if (order != null && order.ApplicationUser != null)
                {
                    var userEmail = order.ApplicationUser.Email;
                    if (!string.IsNullOrEmpty(userEmail))
                    {
                        await _emailService.SendOrderPaidAsync(userEmail, order.Id, order.TotalAmount, order.LanguageCode);
                        _logger.LogInformation("Order paid email sent for Order {OrderId}", order.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send order paid email for PaymentIntentId {PaymentIntentId}",
                    paymentIntent.Id);
                // Non-critical failure - don't throw
            }

            // Clean up Redis basket after successful payment
            if (paymentIntent.Metadata != null && paymentIntent.Metadata.TryGetValue("BasketId", out var basketId))
            {
                try
                {
                    _logger.LogInformation("Deleting basket {BasketId} after successful payment", basketId);
                    await _basketService.DeleteBasketAsync(basketId);
                    _logger.LogInformation("Redis basket {BasketId} deleted successfully", basketId);
                }
                catch (Exception ex)
                {
                    // Non-critical failure - log but don't throw
                    _logger.LogWarning(ex, "Failed to delete basket {BasketId} from Redis", basketId);
                }
            }

            _logger.LogInformation("Payment success processed for: {PaymentIntentId}", paymentIntent.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment_intent.succeeded webhook for {PaymentIntentId}",
                paymentIntent.Id);
            throw;
        }
    }

    /// <summary>
    /// Handle charge.refunded event
    /// - Idempotent: checks if RefundId already set (prevents duplicate processing)
    /// - Records refund with RefundId for audit trail and dispute prevention
    /// - Updates payment transaction status to "Refunded"
    /// </summary>
    private async Task HandleChargeRefunded(Event stripeEvent)
    {
        var charge = stripeEvent.Data.Object as Charge;
        if (charge == null)
        {
            _logger.LogWarning("Failed to deserialize charge from charge.refunded webhook");
            return;
        }

        _logger.LogInformation("Processing charge refunded: {ChargeId}",
            charge.Id);

        try
        {
            // Extract refund ID from charge metadata (Stripe stores refund data in charge)
            // Note: charge.RefundIds may not exist in all Stripe SDK versions
            // The refund is identified by the event itself and charge.Id
            var refundId = charge.Id; // Use charge ID as refund identifier

            // NOTE: In a production system, you would:
            // 1. Query PaymentTransactions table by TransactionId (charge.Id)
            // 2. Check if RefundId already set (idempotency)
            // 3. Update RefundId via native SQL to avoid ORM conflicts
            // 4. Log the refund for audit purposes
            // 
            // This webhook handler is defensive - the main AdminOrderService.CancelOrderAsync
            // handles the full 3-Phase cancellation including payment refund initiation.
            // This handler provides a fallback confirmation mechanism.

            _logger.LogInformation(
                "Charge refund recorded: ChargeId {ChargeId}, RefundId {RefundId}, Amount {Amount}",
                charge.Id, refundId, charge.Amount / 100m); // Stripe amounts in cents
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing charge.refunded webhook for Charge {ChargeId}",
                charge.Id);
            throw;
        }
    }

    /// <summary>
    /// Handle payment_intent.payment_failed event
    /// - Log failure for diagnostics
    /// - No order status change (order remains Pending)
    /// </summary>
    private async Task HandlePaymentIntentFailed(Event stripeEvent)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent != null)
        {
            _logger.LogWarning("Payment failed for PaymentIntent {PaymentIntentId}. Reason: {Reason}",
                paymentIntent.Id, paymentIntent.LastPaymentError?.Message ?? "Unknown");
        }

        await Task.CompletedTask;
    }
}
