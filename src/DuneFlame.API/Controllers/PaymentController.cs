using DuneFlame.Application.Payments.Commands.CreateCheckoutSession;
using DuneFlame.Application.Payments.Commands.CreatePaymentIntent;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/payments")]
[ApiController]
[Authorize]
public class PaymentController(IMediator mediator) : ControllerBase
{
    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    /// <summary>
    /// Create or update payment intent for a basket.
    /// POST /api/v1/payments/{basketId}
    /// </summary>
    [HttpPost("{basketId}")]
    [AllowAnonymous]
    [EnableRateLimiting("CheckoutPolicy")]
    public async Task<IActionResult> CreatePaymentIntent(string basketId)
    {
        try
        {
            var result = await mediator.Send(new CreatePaymentIntentCommand(basketId, GetUserId()));
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "Failed to create payment intent", error = ex.Message }); }
    }

    /// <summary>
    /// Create a Stripe Checkout Session for a product.
    /// POST /api/v1/payments/checkout-session
    /// </summary>
    [HttpPost("checkout-session")]
    [EnableRateLimiting("CheckoutPolicy")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCheckoutSessionCommand command)
    {
        try
        {
            if (GetUserId() == null) return Unauthorized(new { message = "User must be authenticated to use this endpoint" });
            var sessionId = await mediator.Send(command);
            return Ok(new { sessionId });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return BadRequest(new { message = "Failed to create checkout session", error = ex.Message }); }
    }
}