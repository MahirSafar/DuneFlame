using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Master Data Controller - Exposes reference data for UI forms and dropdowns.
/// This includes product attributes, roast levels, grind types, categories, and origins.
/// </summary>
[Route("api/v1/master-data")]
[ApiController]
[AllowAnonymous]
public class MasterDataController(AppDbContext context, ILogger<MasterDataController> logger) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly ILogger<MasterDataController> _logger = logger;

    /// <summary>
    /// Get all available product attributes (e.g. Weight, Color).
    /// </summary>
    [HttpGet("attributes")]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public async Task<ActionResult<List<ProductAttributeDto>>> GetAttributes()
    {
        try
        {
            var attributes = await _context.ProductAttributes
                .AsNoTracking()
                .Include(a => a.Values)
                .OrderBy(a => a.Name)
                .Select(a => new ProductAttributeDto(
                    a.Id, 
                    a.Name,
                    a.Values.Select(v => new ProductAttributeValueDto(v.Id, v.Value)).ToList()
                ))
                .ToListAsync();

            return Ok(attributes);
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

    [HttpGet("categories")]
    [ResponseCache(Duration = 3600, VaryByHeader = "Accept-Language")] // Cache dilə görə dəyişir
    public async Task<ActionResult<List<CategoryDto>>> GetCategories()
    {
        try
        {
            // 1. Header-dən dili oxuyuruq (Frontend-dən gələn 'Accept-Language')
            var header = Request.Headers["Accept-Language"].ToString();
            // Sadələşdirmə: 'ar-SA' gəlsə də, 'ar' götürürük. Yoxdursa 'en'.
            var lang = !string.IsNullOrWhiteSpace(header) && header.StartsWith("ar") ? "ar" : "en";

            var categories = await _context.Categories
                .AsNoTracking()
                .Include(c => c.Translations)
                .OrderBy(c => c.Slug)
                .ToListAsync();

            var categoryDtos = categories.Select(c => new CategoryDto(
                c.Id,
                // 2. Dinamik dil seçimi:
                // Əvvəl istifadəçinin dilini yoxla, tapmasan İngiliscəni, onu da tapmasan "Unknown"
                c.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.Name 
                ?? c.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name 
                ?? "Unknown",
                c.Slug,
                c.IsCoffeeCategory))
                .ToList();

            return Ok(categoryDtos);
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

    [HttpGet("brands")]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public async Task<ActionResult<List<BrandDto>>> GetBrands()
    {
        try
        {
            var brands = await _context.Brands
                .AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .Select(b => new BrandDto(b.Id, b.Name))
                .ToListAsync();

            return Ok(brands);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("all")]
    [ResponseCache(Duration = 3600, VaryByHeader = "Accept-Language")] // Cache dilə görə dəyişir
    public async Task<ActionResult<MasterDataCollectionDto>> GetAllMasterData()
    {
        try
        {
            // 1. Header-dən dili oxuyuruq (Frontend-dən gələn 'Accept-Language')
            var header = Request.Headers["Accept-Language"].ToString();
            // Sadələşdirmə: 'ar-SA' gəlsə də, 'ar' götürürük. Yoxdursa 'en'.
            var lang = !string.IsNullOrWhiteSpace(header) && header.StartsWith("ar") ? "ar" : "en";

            var attributes = await _context.ProductAttributes
                .AsNoTracking()
                .Include(a => a.Values)
                .OrderBy(a => a.Name)
                .Select(a => new ProductAttributeDto(
                    a.Id, 
                    a.Name,
                    a.Values.Select(v => new ProductAttributeValueDto(v.Id, v.Value)).ToList()
                ))
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
                .Include(c => c.Translations)
                .OrderBy(c => c.Slug)
                .ToListAsync();

            var categoryDtos = categories.Select(c => new CategoryDto(
                c.Id,
                // 2. Dinamik dil seçimi:
                // Əvvəl istifadəçinin dilini yoxla, tapmasan İngiliscəni, onu da tapmasan "Unknown"
                c.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.Name 
                ?? c.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name 
                ?? "Unknown",
                c.Slug,
                c.IsCoffeeCategory))
                .ToList();

            var origins = await _context.Origins
                .AsNoTracking()
                .OrderBy(o => o.Name)
                .Select(o => new OriginDto(o.Id, o.Name))
                .ToListAsync();

            var brands = await _context.Brands
                .AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .Select(b => new BrandDto(b.Id, b.Name))
                .ToListAsync();

            var masterData = new MasterDataCollectionDto(
                Attributes: attributes,
                RoastLevels: roastLevels,
                GrindTypes: grindTypes,
                Categories: categoryDtos,
                Origins: origins,
                Brands: brands
            );

            return Ok(masterData);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error fetching master data");
                            return BadRequest(new { message = ex.Message });
                        }
                    }
                }

/// <summary>
/// DTO for ProductAttributeValue master data
/// </summary>
public record ProductAttributeValueDto(Guid Id, string Value);

/// <summary>
/// DTO for ProductAttribute master data
/// </summary>
public record ProductAttributeDto(Guid Id, string Name, List<ProductAttributeValueDto> Values);

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
public record CategoryDto(Guid Id, string Name, string Slug, bool IsCoffeeCategory);

/// <summary>
/// DTO for Origin master data
/// </summary>
public record OriginDto(Guid Id, string Name);

/// <summary>
/// DTO for Brand master data
/// </summary>
public record BrandDto(Guid Id, string Name);

/// <summary>
/// Combined DTO for all master data - used by /api/v1/master-data/all endpoint
/// </summary>
public record MasterDataCollectionDto(
    List<ProductAttributeDto> Attributes,
    List<RoastLevelDto> RoastLevels,
    List<GrindTypeDto> GrindTypes,
    List<CategoryDto> Categories,
    List<OriginDto> Origins,
    List<BrandDto> Brands
);
