using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/basket")]
[ApiController]
public class BasketController(IBasketService basketService, ICurrencyProvider currencyProvider, IProductService productService, IValidator<CustomerBasketDto> validator) : ControllerBase
{
    private readonly IBasketService _basketService = basketService;
    private readonly ICurrencyProvider _currencyProvider = currencyProvider;
    private readonly IProductService _productService = productService;
    private readonly IValidator<CustomerBasketDto> _validator = validator;

    // SEHRBAZ METOD: Giriş etmiş istifadəçilərin ID-sini avtomatik tapır
    // Kimsə başqasının səbətinə nəsə ata bilməsin deyə qoruyur.
    // "me" və ya boş ID göndərilərsə auth userId ilə əvəzlənir.
    // Explicit guest basket ID göndərilirsə — merge üçün — olduğu kimi saxlanılır.
    private string ResolveBasketId(string providedId)
    {
        if (User.Identity is { IsAuthenticated: true } &&
            (string.IsNullOrWhiteSpace(providedId) || providedId.Equals("me", StringComparison.OrdinalIgnoreCase)))
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userId)) return userId;
        }
        return providedId;
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBasket(string id)
    {
        try
        {
            var targetId = ResolveBasketId(id);
            if (string.IsNullOrWhiteSpace(targetId)) return BadRequest(new { message = "Basket ID is required" });

            var basket = await _basketService.GetBasketAsync(targetId);
            basket.CurrencyCode = _currencyProvider.GetCurrentCurrencyCode();
            return Ok(basket);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateBasket([FromBody] CustomerBasketDto basket)
    {
        try
        {
            if (basket == null || string.IsNullOrWhiteSpace(basket.Id)) return BadRequest(new { message = "Basket ID is required" });

            var validationResult = await _validator.ValidateAsync(basket);
            if (!validationResult.IsValid)
                return BadRequest(new { message = validationResult.Errors[0].ErrorMessage });

            // Əgər istifadəçi giriş edibsə, səbət avtomatik onun öz profilinə (JSONB) yazılacaq
            basket.Id = ResolveBasketId(basket.Id);
            basket.CurrencyCode = _currencyProvider.GetCurrentCurrencyCode();

            await _basketService.UpdateBasketAsync(basket);
            return Ok(new { message = "Basket updated successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}/items/{itemId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> DeleteProductFromBasket(string id, Guid itemId)
    {
        try
        {
            var targetId = ResolveBasketId(id);
            await _basketService.RemoveItemFromBasketAsync(targetId, itemId);
            return Ok(new { message = "Basket item removed successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> DeleteBasket(string id)
    {
        try
        {
            var targetId = ResolveBasketId(id);
            await _basketService.DeleteBasketAsync(targetId);
            return Ok(new { message = "Basket deleted successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("recommendation/stateless")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRecommendationStateless([FromBody] UpsellRequestDto request)
    {
        try
        {
            var currentCurrency = _currencyProvider.GetCurrentCurrency().ToString();
            decimal threshold = currentCurrency == "AED" ? 200m : 55m;

            if (request.CurrentSubtotal >= threshold)
            {
                return NoContent();
            }

            decimal gap = threshold - request.CurrentSubtotal;

            var recommendation = await _productService.GetUpsellRecommendationAsync(gap, request.ExcludedProductVariantIds, currentCurrency);

            var response = new UpsellResponseDto
            {
                TargetThreshold = threshold,
                CurrentSubtotal = request.CurrentSubtotal,
                GapAmount = gap,
                Recommendation = recommendation
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
