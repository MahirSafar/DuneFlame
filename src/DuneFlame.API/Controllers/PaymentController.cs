using DuneFlame.Application.DTOs.Payment;
using DuneFlame.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using DuneFlame.Infrastructure.Persistence;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/payments")]
[ApiController]
[Authorize]
public class PaymentController(
    IPaymentService paymentService,
    AppDbContext context,
    IValidator<CreatePaymentIntentRequest> createPaymentIntentValidator) : ControllerBase
{
    private readonly IPaymentService _paymentService = paymentService;
    private readonly AppDbContext _context = context;
    private readonly IValidator<CreatePaymentIntentRequest> _createPaymentIntentValidator = createPaymentIntentValidator;

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in claims");
        }
        return userId;
    }

    [HttpPost("create-intent")]
    [EnableRateLimiting("CheckoutPolicy")]
    public async Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentIntentRequest request)
    {
        var validationResult = await _createPaymentIntentValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        try
        {
            var userId = GetUserId();

            // Validate that the order belongs to the current user
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId && o.UserId == userId);
            if (order == null)
            {
                return NotFound(new { message = "Order not found or does not belong to you" });
            }

            // Check if order is already paid
            if (order.Status.ToString() == "Paid")
            {
                return BadRequest(new { message = "Order is already paid" });
            }

            // Create PaymentTransaction record
            var paymentTransaction = new DuneFlame.Domain.Entities.PaymentTransaction
            {
                OrderId = request.OrderId,
                Amount = order.TotalAmount,
                Currency = "usd",
                Status = "Pending",
                PaymentMethod = string.Empty
            };

            _context.PaymentTransactions.Add(paymentTransaction);
            await _context.SaveChangesAsync();

            // Create Stripe Payment Intent
            var paymentIntent = await _paymentService.CreatePaymentIntentAsync(
                order.TotalAmount,
                "usd",
                request.OrderId);

            // Update PaymentTransaction with Stripe TransactionId
            paymentTransaction.TransactionId = paymentIntent.PaymentIntentId;
            _context.PaymentTransactions.Update(paymentTransaction);
            await _context.SaveChangesAsync();

            var response = new PaymentIntentDto(
                paymentIntent.ClientSecret,
                paymentIntent.PaymentIntentId,
                order.TotalAmount);

            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
