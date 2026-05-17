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
/// All endpoints resolve translated names from the Accept-Language header (ar/en), with
/// English fallback and raw entity Name as final fallback.
/// </summary>
[Route("api/v1/master-data")]
[ApiController]
[AllowAnonymous]
public class MasterDataController(AppDbContext context, ILogger<MasterDataController> logger) : ControllerBase
{
    private readonly AppDbContext _context = context;
    private readonly ILogger<MasterDataController> _logger = logger;

    /// <summary>
    /// Reads and normalises the Accept-Language header to a 2-char code.
    /// Supports "ar" and "en"; all other values fall back to "en".
    /// </summary>
    private string ExtractLanguage()
    {
        var header = Request.Headers["Accept-Language"].ToString();
        if (string.IsNullOrWhiteSpace(header)) return "en";
        var lang = header.Split(',')[0].Trim();
        lang = lang.Length >= 2 ? lang[..2].ToLower() : "en";
        return lang == "ar" ? "ar" : "en";
    }

    /// <summary>
    /// Get all available product attributes (e.g. Weight, Color) with translated names and values.
    /// </summary>
    [HttpGet("attributes")]
    [ResponseCache(Duration = 3600, VaryByHeader = "Accept-Language")]
    public async Task<ActionResult<List<ProductAttributeDto>>> GetAttributes()
    {
        try
        {
            var lang = ExtractLanguage();

            var attributes = await _context.ProductAttributes
                .AsNoTracking()
                .Include(a => a.Translations)
                .Include(a => a.Values)
                    .ThenInclude(v => v.Translations)
                .OrderBy(a => a.Name)
                .ToListAsync();

            var dtos = attributes.Select(a =>
            {
                var attrName = a.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                            ?? a.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                            ?? a.Name;

                var valueDtos = a.Values.Select(v =>
                {
                    var val = v.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedValue
                           ?? v.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedValue
                           ?? v.Value;
                    return new ProductAttributeValueDto(v.Id, val);
                }).ToList();

                return new ProductAttributeDto(a.Id, attrName, valueDtos);
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get all available roast levels with translated names.
    /// </summary>
    [HttpGet("roast-levels")]
    [ResponseCache(Duration = 3600, VaryByHeader = "Accept-Language")]
    public async Task<ActionResult<List<RoastLevelDto>>> GetRoastLevels()
    {
        try
        {
            var lang = ExtractLanguage();

            var roastLevels = await _context.RoastLevels
                .AsNoTracking()
                .Include(r => r.Translations)
                .OrderBy(r => r.Name)
                .ToListAsync();

            var dtos = roastLevels.Select(r =>
            {
                var name = r.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                        ?? r.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                        ?? r.Name;
                return new RoastLevelDto(r.Id, name);
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get all available grind types with translated names.
    /// </summary>
    [HttpGet("grind-types")]
    [ResponseCache(Duration = 3600, VaryByHeader = "Accept-Language")]
    public async Task<ActionResult<List<GrindTypeDto>>> GetGrindTypes()
    {
        try
        {
            var lang = ExtractLanguage();

            var grindTypes = await _context.GrindTypes
                .AsNoTracking()
                .Include(g => g.Translations)
                .OrderBy(g => g.Name)
                .ToListAsync();

            var dtos = grindTypes.Select(g =>
            {
                var name = g.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                        ?? g.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                        ?? g.Name;
                return new GrindTypeDto(g.Id, name);
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("categories")]
    [ResponseCache(Duration = 3600, VaryByHeader = "Accept-Language")]
    public async Task<ActionResult<List<CategoryDto>>> GetCategories()
    {
        try
        {
            var lang = ExtractLanguage();

            var categories = await _context.Categories
                .AsNoTracking()
                .Include(c => c.Translations)
                .OrderBy(c => c.Slug)
                .ToListAsync();

            var categoryDtos = categories.Select(c =>
            {
                var name = c.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.Name
                        ?? c.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                        ?? "Unknown";
                return new CategoryDto(c.Id, name, c.Slug, c.IsCoffeeCategory,
                    c.ParentCategoryId == Guid.Empty ? null : c.ParentCategoryId);
            }).ToList();

            return Ok(categoryDtos);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get all available product origins with translated names.
    /// </summary>
    [HttpGet("origins")]
    [ResponseCache(Duration = 3600, VaryByHeader = "Accept-Language")]
    public async Task<ActionResult<List<OriginDto>>> GetOrigins()
    {
        try
        {
            var lang = ExtractLanguage();

            var origins = await _context.Origins
                .AsNoTracking()
                .Include(o => o.Translations)
                .OrderBy(o => o.Name)
                .ToListAsync();

            var dtos = origins.Select(o =>
            {
                var name = o.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                        ?? o.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                        ?? o.Name;
                return new OriginDto(o.Id, name);
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("brands")]
    [ResponseCache(Duration = 3600, VaryByHeader = "Accept-Language")]
    public async Task<ActionResult<List<BrandDto>>> GetBrands()
    {
        try
        {
            var lang = ExtractLanguage();

            var brands = await _context.Brands
                .AsNoTracking()
                .Include(b => b.Translations)
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();

            var dtos = brands.Select(b =>
            {
                var name = b.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                        ?? b.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                        ?? b.Name;
                return new BrandDto(b.Id, name);
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("all")]
    [ResponseCache(Duration = 3600, VaryByHeader = "Accept-Language")]
    public async Task<ActionResult<MasterDataCollectionDto>> GetAllMasterData()
    {
        try
        {
            var lang = ExtractLanguage();

            var attributeEntities = await _context.ProductAttributes
                .AsNoTracking()
                .Include(a => a.Translations)
                .Include(a => a.Values)
                    .ThenInclude(v => v.Translations)
                .OrderBy(a => a.Name)
                .ToListAsync();

            var attributes = attributeEntities.Select(a =>
            {
                var attrName = a.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                            ?? a.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                            ?? a.Name;
                var valueDtos = a.Values.Select(v =>
                {
                    var val = v.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedValue
                           ?? v.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedValue
                           ?? v.Value;
                    return new ProductAttributeValueDto(v.Id, val);
                }).ToList();
                return new ProductAttributeDto(a.Id, attrName, valueDtos);
            }).ToList();

            var roastLevelEntities = await _context.RoastLevels
                .AsNoTracking()
                .Include(r => r.Translations)
                .OrderBy(r => r.Name)
                .ToListAsync();

            var roastLevels = roastLevelEntities.Select(r =>
            {
                var name = r.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                        ?? r.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                        ?? r.Name;
                return new RoastLevelDto(r.Id, name);
            }).ToList();

            var grindTypeEntities = await _context.GrindTypes
                .AsNoTracking()
                .Include(g => g.Translations)
                .OrderBy(g => g.Name)
                .ToListAsync();

            var grindTypes = grindTypeEntities.Select(g =>
            {
                var name = g.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                        ?? g.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                        ?? g.Name;
                return new GrindTypeDto(g.Id, name);
            }).ToList();

            var categoryEntities = await _context.Categories
                .AsNoTracking()
                .Include(c => c.Translations)
                .OrderBy(c => c.Slug)
                .ToListAsync();

            var categories = categoryEntities.Select(c =>
            {
                var name = c.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.Name
                        ?? c.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                        ?? "Unknown";
                return new CategoryDto(c.Id, name, c.Slug, c.IsCoffeeCategory,
                    c.ParentCategoryId == Guid.Empty ? null : c.ParentCategoryId);
            }).ToList();

            var originEntities = await _context.Origins
                .AsNoTracking()
                .Include(o => o.Translations)
                .OrderBy(o => o.Name)
                .ToListAsync();

            var origins = originEntities.Select(o =>
            {
                var name = o.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                        ?? o.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                        ?? o.Name;
                return new OriginDto(o.Id, name);
            }).ToList();

            var brandEntities = await _context.Brands
                .AsNoTracking()
                .Include(b => b.Translations)
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();

            var brands = brandEntities.Select(b =>
            {
                var name = b.Translations.FirstOrDefault(t => t.LanguageCode == lang)?.TranslatedName
                        ?? b.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                        ?? b.Name;
                return new BrandDto(b.Id, name);
            }).ToList();

            return Ok(new MasterDataCollectionDto(attributes, roastLevels, grindTypes, categories, origins, brands));
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
public record CategoryDto(Guid Id, string Name, string Slug, bool IsCoffeeCategory, Guid? ParentCategoryId);

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
