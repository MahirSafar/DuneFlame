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
    IBasketService basketService,
    AppDbContext context,
    IValidator<CreatePaymentIntentRequest> createPaymentIntentValidator) : ControllerBase
{
    private readonly IPaymentService _paymentService = paymentService;
    private readonly IBasketService _basketService = basketService;
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

    /// <summary>
    /// Create or update payment intent for a basket
    /// POST /api/v1/payments/{basketId}
    /// </summary>
    [HttpPost("{basketId}")]
    [EnableRateLimiting("CheckoutPolicy")]
    public async Task<IActionResult> CreatePaymentIntent(string basketId)
    {
        try
        {
            var userId = GetUserId();

            // Fetch basket from Redis
            var basket = await _basketService.GetBasketAsync(basketId);
            if (basket == null || basket.Items.Count == 0)
            {
                return BadRequest(new { message = "Basket is empty or not found" });
            }

            // Calculate total amount from basket items
            decimal totalAmount = 0;
            foreach (var item in basket.Items)
            {
                totalAmount += item.Price * item.Quantity;
            }

            if (totalAmount <= 0)
            {
                return BadRequest(new { message = "Invalid basket total" });
            }

            // Create or update PaymentIntent
            var paymentIntent = await _paymentService.CreateOrUpdatePaymentIntentAsync(
                basketId,
                totalAmount,
                basket.CurrencyCode.ToString().ToLower());

            // Sync PaymentIntentId with the latest pending order for this user
            // Find the most recently created pending order and update it with the PaymentIntentId
            var order = await _context.Orders
                .Where(o => o.UserId == userId && o.Status == DuneFlame.Domain.Enums.OrderStatus.Pending)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (order != null && string.IsNullOrEmpty(order.PaymentIntentId))
            {
                order.PaymentIntentId = paymentIntent.PaymentIntentId;
                _context.Orders.Update(order);
                await _context.SaveChangesAsync();
            }

            var response = new DuneFlame.Application.DTOs.Payment.PaymentIntentDto(
                paymentIntent.ClientSecret,
                paymentIntent.PaymentIntentId,
                totalAmount);

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Failed to create payment intent", error = ex.Message });
        }
    }

    /// <summary>
    /// Create payment intent for an order (legacy endpoint)
    /// POST /api/v1/payments/create-intent
    /// </summary>
    [HttpPost("create-intent")]
    [EnableRateLimiting("CheckoutPolicy")]
    [Obsolete("Use POST /api/v1/payments/{basketId} instead")]
    public async Task<IActionResult> CreatePaymentIntentLegacy([FromBody] CreatePaymentIntentRequest request)
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
                CurrencyCode = order.CurrencyCode,
                Status = "Pending",
                PaymentMethod = string.Empty
            };

            _context.PaymentTransactions.Add(paymentTransaction);
            await _context.SaveChangesAsync();

            // Create Stripe Payment Intent
            var paymentIntent = await _paymentService.CreatePaymentIntentAsync(
                order.TotalAmount,
                order.CurrencyCode.ToString().ToLower(),
                request.OrderId);

            // Update PaymentTransaction with Stripe TransactionId
            paymentTransaction.TransactionId = paymentIntent.PaymentIntentId;
            _context.PaymentTransactions.Update(paymentTransaction);
            await _context.SaveChangesAsync();

            var response = new DuneFlame.Application.DTOs.Payment.PaymentIntentDto(
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
                            return BadRequest(new { message = "Failed to create payment intent", error = ex.Message });
                        }
                    }
                }
