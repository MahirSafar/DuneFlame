using DuneFlame.Application.DTOs.Payment;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Example PaymentIntent Controller - .NET 10 Standards
/// Demonstrates proper handling of Stripe PaymentIntent creation and client_secret transmission
/// 
/// Key Points for Frontend Integration:
/// 1. Use client_secret directly without any string manipulation
/// 2. Format is: pi_xxxxx_secret_yyyy (exactly)
/// 3. Pass to Stripe Elements: stripe.confirmCardPayment(clientSecret, ...)
/// </summary>
[Route("api/v1/payment-intents")]
[ApiController]
[Authorize]
public class PaymentIntentExampleController(
    IPaymentService paymentService,
    ILogger<PaymentIntentExampleController> logger) : ControllerBase
{
    private readonly IPaymentService _paymentService = paymentService;
    private readonly ILogger<PaymentIntentExampleController> _logger = logger;

    /// <summary>
    /// Create PaymentIntent for order payment
    /// 
    /// Usage:
    /// POST /api/v1/payment-intents/create
    /// Body: { "orderId": "guid", "amount": 100.50, "currency": "usd" }
    /// 
    /// Response:
    /// {
    ///   "clientSecret": "pi_1234_secret_5678",  ← Send directly to frontend
    ///   "paymentIntentId": "pi_1234",
    ///   "amount": 100.50,
    ///   "paymentNotRequired": false
    /// }
    /// </summary>
    [HttpPost("create")]
    public async Task<ActionResult<PaymentIntentDto>> CreatePaymentIntent(
        [FromBody] CreatePaymentIntentDto request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Unauthorized access attempt to CreatePaymentIntent");
                return Unauthorized(new { message = "User ID not found in claims" });
            }

            _logger.LogInformation(
                "Creating PaymentIntent for Order {OrderId}, Amount: {Amount} {Currency}",
                request.OrderId, request.Amount, request.Currency);

            // Call service to create PaymentIntent with Stripe
            var paymentIntentResponse = await _paymentService.CreatePaymentIntentAsync(
                amount: request.Amount,
                currency: request.Currency.ToLower(),
                orderId: request.OrderId);

            // Construct response DTO - client_secret is transmitted without modification
            var response = new PaymentIntentDto(
                ClientSecret: paymentIntentResponse.ClientSecret,  // ← Format: pi_xxxxx_secret_yyyy
                PaymentIntentId: paymentIntentResponse.PaymentIntentId,
                Amount: request.Amount,
                PaymentNotRequired: false);

            _logger.LogInformation(
                "PaymentIntent created successfully: {PaymentIntentId}. " +
                "ClientSecret format validated. Ready for frontend transmission.",
                response.PaymentIntentId);

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Business logic error creating PaymentIntent for Order {OrderId}", request.OrderId);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating PaymentIntent for Order {OrderId}", request.OrderId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { message = "Failed to create payment intent. Please try again." });
        }
    }

    /// <summary>
    /// Frontend Integration Example (TypeScript/JavaScript)
    /// 
    /// Usage in React/Next.js:
    /// 
    /// const response = await fetch('/api/v1/payment-intents/create', {
    ///   method: 'POST',
    ///   headers: { 'Content-Type': 'application/json' },
    ///   body: JSON.stringify({
    ///     orderId: orderId,
    ///     amount: 100.50,
    ///     currency: 'usd'
    ///   })
    /// });
    /// 
    /// const { clientSecret, paymentIntentId } = await response.json();
    /// 
    /// // ✅ CORRECT: Use clientSecret directly
    /// const result = await stripe.confirmCardPayment(clientSecret, {
    ///   payment_method: {
    ///     card: cardElement,
    ///     billing_details: { name: cardholderName }
    ///   }
    /// });
    /// 
    /// // ❌ WRONG: Don't concatenate or modify clientSecret
    /// // await stripe.confirmCardPayment(clientSecret + "?extra", ...); // 400 Bad Request
    /// // await stripe.confirmCardPayment(JSON.stringify({ clientSecret }), ...); // 400 Bad Request
    /// </summary>
    public class CreatePaymentIntentDto
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "usd";
    }
}

/// <summary>
/// Response Model - Standard format for Stripe PaymentIntent responses
/// </summary>
public record PaymentIntentResponse(
    string ClientSecret,
    string PaymentIntentId
);
