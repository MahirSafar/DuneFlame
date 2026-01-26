using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Master Data Controller - Exposes reference data for UI forms and dropdowns.
/// This includes product weights, roast levels, grind types, categories, and origins.
/// Data is cached for performance as it changes infrequently.
/// </summary>
[Route("api/v1/master-data")]
[ApiController]
[AllowAnonymous]
public class MasterDataController(AppDbContext context) : ControllerBase
{
    private readonly AppDbContext _context = context;

    /// <summary>
    /// Get all available product weights for weight selection in product forms.
    /// </summary>
    /// <returns>List of weights (Id, Label, Grams)</returns>
    [HttpGet("weights")]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public async Task<ActionResult<List<ProductWeightDto>>> GetWeights()
    {
        try
        {
            var weights = await _context.ProductWeights
                .AsNoTracking()
                .OrderBy(w => w.Grams)
                .Select(w => new ProductWeightDto(w.Id, w.Label, w.Grams))
                .ToListAsync();

            return Ok(weights);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get all available roast levels for roast selection in product forms.
    /// </summary>
    /// <returns>List of roast levels (Id, Name)</returns>
    [HttpGet("roast-levels")]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public async Task<ActionResult<List<RoastLevelDto>>> GetRoastLevels()
    {
        try
        {
            var roastLevels = await _context.RoastLevels
                .AsNoTracking()
                .OrderBy(r => r.Name)
                .Select(r => new RoastLevelDto(r.Id, r.Name))
                .ToListAsync();

            return Ok(roastLevels);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get all available grind types for grind selection in product forms.
    /// </summary>
    /// <returns>List of grind types (Id, Name)</returns>
    [HttpGet("grind-types")]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public async Task<ActionResult<List<GrindTypeDto>>> GetGrindTypes()
    {
        try
        {
            var grindTypes = await _context.GrindTypes
                .AsNoTracking()
                .OrderBy(g => g.Name)
                .Select(g => new GrindTypeDto(g.Id, g.Name))
                .ToListAsync();

            return Ok(grindTypes);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get all available product categories for category selection in product forms.
    /// </summary>
    /// <returns>List of categories (Id, Name, Slug)</returns>
    [HttpGet("categories")]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public async Task<ActionResult<List<CategoryDto>>> GetCategories()
    {
        try
        {
            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryDto(c.Id, c.Name, c.Slug))
                .ToListAsync();

            return Ok(categories);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get all available product origins for origin selection in product forms.
    /// </summary>
    /// <returns>List of origins (Id, Name)</returns>
    [HttpGet("origins")]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public async Task<ActionResult<List<OriginDto>>> GetOrigins()
    {
        try
        {
            var origins = await _context.Origins
                .AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new OriginDto(o.Id, o.Name))
                .ToListAsync();

            return Ok(origins);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get all master data at once for efficient initial page load.
    /// Useful when the frontend needs multiple master data lists.
    /// </summary>
    /// <returns>Combined master data object</returns>
    [HttpGet("all")]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public async Task<ActionResult<MasterDataCollectionDto>> GetAllMasterData()
    {
        try
        {
            var weights = await _context.ProductWeights
                .AsNoTracking()
                .OrderBy(w => w.Grams)
                .Select(w => new ProductWeightDto(w.Id, w.Label, w.Grams))
                .ToListAsync();

            var roastLevels = await _context.RoastLevels
                .AsNoTracking()
                .OrderBy(r => r.Name)
                .Select(r => new RoastLevelDto(r.Id, r.Name))
                .ToListAsync();

            var grindTypes = await _context.GrindTypes
                .AsNoTracking()
                .OrderBy(g => g.Name)
                .Select(g => new GrindTypeDto(g.Id, g.Name))
                .ToListAsync();

            var categories = await _context.Categories
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => new CategoryDto(c.Id, c.Name, c.Slug))
                .ToListAsync();

            var origins = await _context.Origins
                .AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new OriginDto(o.Id, o.Name))
                .ToListAsync();

            var masterData = new MasterDataCollectionDto(
                Weights: weights,
                RoastLevels: roastLevels,
                GrindTypes: grindTypes,
                Categories: categories,
                Origins: origins
            );

            return Ok(masterData);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

/// <summary>
/// DTO for ProductWeight master data
/// </summary>
public record ProductWeightDto(Guid Id, string Label, int Grams);

/// <summary>
/// DTO for RoastLevel master data
/// </summary>
public record RoastLevelDto(Guid Id, string Name);

/// <summary>
/// DTO for GrindType master data
/// </summary>
public record GrindTypeDto(Guid Id, string Name);

/// <summary>
/// DTO for Category master data
/// </summary>
public record CategoryDto(Guid Id, string Name, string Slug);

/// <summary>
/// DTO for Origin master data
/// </summary>
public record OriginDto(Guid Id, string Name);

/// <summary>
/// Combined DTO for all master data - used by /api/v1/master-data/all endpoint
/// </summary>
public record MasterDataCollectionDto(
    List<ProductWeightDto> Weights,
    List<RoastLevelDto> RoastLevels,
    List<GrindTypeDto> GrindTypes,
    List<CategoryDto> Categories,
    List<OriginDto> Origins
);
