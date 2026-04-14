using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Products.Commands.UpdateProduct;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DuneFlame.API.Controllers;

[Route("api/v1/admin/products")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminProductController(
    IMediator mediator,
    IProductService productService,
    IValidator<CreateProductRequest> createProductValidator,
    ILogger<AdminProductController> logger) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly IProductService _productService = productService;
    private readonly IValidator<CreateProductRequest> _createProductValidator = createProductValidator;
    private readonly ILogger<AdminProductController> _logger = logger;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> GetAllProducts(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null)
    {
        try
        {
            var result = await _productService.GetAllAdminAsync(
                pageNumber: pageNumber,
                pageSize: pageSize,
                sortBy: sortBy,
                search: search,
                categoryId: categoryId,
                minPrice: minPrice,
                maxPrice: maxPrice);

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
    public async Task<IActionResult> UpdateProduct(
        Guid id, 
        [FromForm] UpdateProductCommand command, 
        [FromServices] IValidator<UpdateProductCommand> validator)
    {
        if (id != command.Id)
            return BadRequest(new { message = "ID in route does not match ID in command." });

        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return BadRequest(validationResult.Errors);

        try
        {
            await _mediator.Send(command);
            return Ok(new { message = "Product updated successfully." });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (ex.Entries.Any())
            {
                var entry = ex.Entries.Single();
                _logger.LogError("CONCURRENCY CRASH! Table: {Table}. Entry ID: {Id}", 
                    entry.Metadata.Name, entry.Property("Id").CurrentValue);
            }
            else
            {
                _logger.LogError("CONCURRENCY CRASH! No entries available in the exception.");
            }
            throw; // Re-throw to see the full stack trace
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

    [HttpPatch("{id:guid}/restore")]
    public async Task<IActionResult> RestoreProduct(Guid id)
    {
        try
        {
            await _productService.RestoreAsync(id);
            return Ok(new { message = "Product restored successfully." });
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
