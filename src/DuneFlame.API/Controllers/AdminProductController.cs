using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Products.Commands.CreateProduct;
using DuneFlame.Application.Products.Commands.DeleteProduct;
using DuneFlame.Application.Products.Commands.RestoreProduct;
using DuneFlame.Application.Products.Commands.UpdateProduct;
using DuneFlame.Application.Products.Queries.GetAllProducts;
using DuneFlame.Application.Products.Queries.GetProductById;
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
    ILogger<AdminProductController> logger) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<AdminProductController> _logger = logger;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> GetAllProducts(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
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
                AdminView: true));

            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateProduct(
        [FromForm] CreateProductCommand command,
        [FromServices] IValidator<CreateProductCommand> validator)
    {
        var validationResult = await validator.ValidateAsync(command);
        if (!validationResult.IsValid)
            return BadRequest(validationResult.Errors);

        try
        {
            var productId = await _mediator.Send(command);
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
            throw;
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
            await _mediator.Send(new DeleteProductCommand(id));
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
            await _mediator.Send(new RestoreProductCommand(id));
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

    [HttpGet("{id:guid}", Name = "GetProductById")]
    [AllowAnonymous]
    public async Task<IActionResult> GetProductById(Guid id)
    {
        try
        {
            var product = await _mediator.Send(new GetProductByIdQuery(id));
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
