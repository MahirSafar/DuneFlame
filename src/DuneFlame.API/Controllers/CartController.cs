using DuneFlame.Application.DTOs.Cart;
using DuneFlame.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/cart")]
[ApiController]
[Authorize]
public class CartController(
    ICartService cartService,
    IValidator<AddToCartRequest> addToCartValidator) : ControllerBase
{
    private readonly ICartService _cartService = cartService;
    private readonly IValidator<AddToCartRequest> _addToCartValidator = addToCartValidator;

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("User ID not found in claims");
        }
        return userId;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyCart()
    {
        try
        {
            var userId = GetUserId();
            var cart = await _cartService.GetMyCartAsync(userId);
            return Ok(cart);
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

    [HttpPost("items")]
    public async Task<IActionResult> AddToCart([FromBody] AddToCartRequest request)
    {
        var validationResult = await _addToCartValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        try
        {
            var userId = GetUserId();
            var cart = await _cartService.AddToCartAsync(userId, request);
            return Ok(cart);
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

        [HttpDelete("items/{itemId:guid}")]
        public async Task<IActionResult> RemoveItem(Guid itemId)
        {
            try
            {
                var userId = GetUserId();
                var cart = await _cartService.RemoveFromCartAsync(userId, itemId);
                return Ok(cart);
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
        /// Bulk sync cart items. Groups items by variation and sums quantities (stacking fix).
        /// This endpoint should be called when frontend does a bulk update to prevent duplicate rows.
        /// </summary>
        [HttpPost("sync")]
        public async Task<IActionResult> SyncCartItems([FromBody] List<AddToCartRequest> items)
        {
            if (items == null || items.Count == 0)
            {
                return BadRequest(new { message = "Items list cannot be empty" });
            }

            try
            {
                var userId = GetUserId();
                var cart = await _cartService.SyncCartItemsAsync(userId, items);
                return Ok(cart);
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
