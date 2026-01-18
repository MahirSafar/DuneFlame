using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/products")]
[ApiController]
[AllowAnonymous]
public class PublicProductController(IProductService productService) : ControllerBase
{
    private readonly IProductService _productService = productService;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> GetAllProducts(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] Guid? categoryId = null)
    {
        try
        {
            var result = await _productService.GetAllAsync(
                pageNumber: pageNumber,
                pageSize: pageSize,
                sortBy: sortBy,
                search: search,
                categoryId: categoryId);

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
                // Try to parse as GUID first
                if (Guid.TryParse(idOrSlug, out var productId))
                {
                    var product = await _productService.GetByIdAsync(productId);
                    return Ok(product);
                }

                // Otherwise, treat as slug
                var productBySlug = await _productService.GetBySlugAsync(idOrSlug);
                return Ok(productBySlug);
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
                var product = await _productService.GetBySlugAsync(slug);
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
