using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Products.Queries.GetAllProducts;
using DuneFlame.Application.Products.Queries.GetProductById;
using DuneFlame.Application.Products.Queries.GetProductBySlug;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/products")]
[ApiController]
[AllowAnonymous]
public class PublicProductController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> GetAllProducts(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 8,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null,
        [FromQuery] Guid? brandId = null,
        [FromQuery] Guid[]? roastLevelIds = null,
        [FromQuery] Guid[]? originIds = null)
    {
        try
        {
            var result = await _mediator.Send(new GetAllProductsQuery(
                PageNumber: pageNumber,
                PageSize: pageSize,
                SortBy: sortBy,
                Search: search,
                CategoryId: categoryId,
                MinPrice: minPrice,
                MaxPrice: maxPrice,
                BrandId: brandId,
                RoastLevelIds: roastLevelIds,
                OriginIds: originIds,
                AdminView: false));

            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("{idOrSlug}")]
    public async Task<ActionResult<ProductResponse>> GetProduct(string idOrSlug)
    {
        try
        {
            ProductResponse product;
            if (Guid.TryParse(idOrSlug, out var productId))
                product = await _mediator.Send(new GetProductByIdQuery(productId));
            else
                product = await _mediator.Send(new GetProductBySlugQuery(idOrSlug));

            return Ok(product);
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

    [HttpGet("by-slug/{slug}")]
    public async Task<ActionResult<ProductResponse>> GetProductBySlug(string slug)
    {
        try
        {
            var product = await _mediator.Send(new GetProductBySlugQuery(slug));
            return Ok(product);
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
