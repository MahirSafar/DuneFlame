using DuneFlame.Application.Baskets.Commands.DeleteBasket;
using DuneFlame.Application.Baskets.Commands.RemoveBasketItem;
using DuneFlame.Application.Baskets.Commands.UpdateBasket;
using DuneFlame.Application.Baskets.Queries.GetBasket;
using DuneFlame.Application.Baskets.Queries.GetUpsellRecommendation;
using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DuneFlame.API.Controllers;

[Route("api/v1/basket")]
[ApiController]
public class BasketController(
    IMediator mediator,
    ICurrencyProvider currencyProvider,
    IValidator<CustomerBasketDto> validator) : ControllerBase
{
    // "me" or empty → resolved to the authenticated user's ID
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
            var basket = await mediator.Send(new GetBasketQuery(targetId));
            basket.CurrencyCode = currencyProvider.GetCurrentCurrencyCode();
            return Ok(basket);
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateBasket([FromBody] CustomerBasketDto basket)
    {
        try
        {
            if (basket == null || string.IsNullOrWhiteSpace(basket.Id))
                return BadRequest(new { message = "Basket ID is required" });
            var validationResult = await validator.ValidateAsync(basket);
            if (!validationResult.IsValid)
                return BadRequest(new { message = validationResult.Errors[0].ErrorMessage });
            basket.Id = ResolveBasketId(basket.Id);
            basket.CurrencyCode = currencyProvider.GetCurrentCurrencyCode();
            await mediator.Send(new UpdateBasketCommand(basket));
            return Ok(new { message = "Basket updated successfully" });
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id}/items/{itemId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> DeleteProductFromBasket(string id, Guid itemId)
    {
        try
        {
            var targetId = ResolveBasketId(id);
            await mediator.Send(new RemoveBasketItemCommand(targetId, itemId));
            return Ok(new { message = "Basket item removed successfully" });
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> DeleteBasket(string id)
    {
        try
        {
            var targetId = ResolveBasketId(id);
            await mediator.Send(new DeleteBasketCommand(targetId));
            return Ok(new { message = "Basket deleted successfully" });
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("recommendation/stateless")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRecommendationStateless([FromBody] UpsellRequestDto request)
    {
        try
        {
            var currentCurrency = currencyProvider.GetCurrentCurrency().ToString();
            decimal threshold = currentCurrency == "AED" ? 200m : 55m;
            if (request.CurrentSubtotal >= threshold) return NoContent();
            decimal gap = threshold - request.CurrentSubtotal;
            var recommendation = await mediator.Send(
                new GetUpsellRecommendationQuery(gap, request.ExcludedProductVariantIds, currentCurrency));
            return Ok(new UpsellResponseDto
            {
                TargetThreshold = threshold,
                CurrentSubtotal = request.CurrentSubtotal,
                GapAmount = gap,
                Recommendation = recommendation
            });
        }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }
}
