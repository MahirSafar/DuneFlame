using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/products")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminProductController(
    IProductService productService,
    IValidator<CreateProductRequest> createProductValidator,
    IValidator<UpdateProductRequest> updateProductValidator) : ControllerBase
{
    private readonly IProductService _productService = productService;
    private readonly IValidator<CreateProductRequest> _createProductValidator = createProductValidator;
    private readonly IValidator<UpdateProductRequest> _updateProductValidator = updateProductValidator;

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

    [HttpPost]
    public async Task<IActionResult> CreateProduct([FromForm] CreateProductRequest request)
    {
        var validationResult = await _createProductValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return BadRequest(validationResult.Errors);

        try
        {
            var productId = await _productService.CreateAsync(request);
            return CreatedAtAction(nameof(GetProductById), new { id = productId }, new { id = productId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProduct(Guid id, [FromForm] UpdateProductRequest request)
    {
        var validationResult = await _updateProductValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
            return BadRequest(validationResult.Errors);

        try
        {
            await _productService.UpdateAsync(id, request);
            return Ok(new { message = "Product updated successfully." });
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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid id)
    {
        try
        {
            await _productService.DeleteAsync(id);
            return Ok(new { message = "Product deleted successfully." });
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

    // Helper endpoint to get product by ID (for location header)
    [HttpGet("{id:guid}", Name = "GetProductById")]
    [AllowAnonymous]
    public async Task<IActionResult> GetProductById(Guid id)
    {
        try
        {
            var product = await _productService.GetByIdAsync(id);
            return Ok(product);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
