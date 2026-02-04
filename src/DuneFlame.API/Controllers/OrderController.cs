using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/orders")]
[ApiController]
[Authorize]
public class OrderController(
    IOrderService orderService,
    IValidator<CreateOrderRequest> createOrderValidator) : ControllerBase
{
    private readonly IOrderService _orderService = orderService;
    private readonly IValidator<CreateOrderRequest> _createOrderValidator = createOrderValidator;

    private Guid? GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return null; // Guest user - no ID
        }
        return userId;
    }

    /// <summary>
    /// Create a new order from basket with shipping address and optional payment intent ID
    /// POST /api/v1/orders
    /// Body: { "basketId": "user-id", "paymentIntentId": "pi_...", "shippingAddress": { "street": "...", "city": "...", ... } }
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var validationResult = await _createOrderValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        try
        {
            var userId = GetUserId();
            var order = await _orderService.CreateOrderAsync(userId, request);
            return CreatedAtAction(nameof(GetOrderById), new { id = order.Id }, order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get all orders for the current user
    /// GET /api/v1/orders
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyOrders()
    {
        try
        {
            var userId = GetUserId();
            if (userId == null)
            {
                return Unauthorized(new { message = "User must be logged in to view orders" });
            }
            var orders = await _orderService.GetMyOrdersAsync(userId.Value);
            return Ok(orders);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific order by ID
    /// GET /api/v1/orders/{id}
    /// Supports both authenticated and guest users:
    /// - If order has UserId (registered user): Only that user can view
    /// - If order has no UserId (guest checkout): Anyone can view (guest knows their order ID from confirmation)
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetOrderById(Guid id)
    {
        try
        {
            var order = await _orderService.GetOrderByIdAsync(id);

            // Security check: Verify access permissions
            var currentUserId = GetUserId();

            if (order.UserId.HasValue)
            {
                // Order belongs to a registered user - only that user can view it
                if (currentUserId == null || currentUserId != order.UserId)
                {
                    return Forbid();
                }
            }
            // else: Order is from guest checkout (UserId is null) - allow any viewer
            // Guest users identify themselves by knowing the order ID

            return Ok(order);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
