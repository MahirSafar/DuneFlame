using DuneFlame.Application.Common;
using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Product service with HybridCache support and currency-aware pricing.
/// 
/// Currency: Determined by ICurrencyProvider (from X-Currency header).
/// Language (Reading): Determined by Accept-Language header (defaults to "en").
/// 
/// Language (Writing/Create/Update):
/// - If request.Translations is provided: Uses specified language codes (normalized to 2-char format).
/// - If request.Translations is null/empty: Uses Accept-Language header to determine the language
///   for the provided Name/Description. This ensures data consistency even when frontend
///   doesn't explicitly provide translations.
/// 
/// All language codes are normalized to 2-char format per Copilot Instructions.
/// Example: "en-US" ? "en", "ar-SA" ? "ar"
/// </summary>
public class ProductService(
    AppDbContext context,
    IFileService fileService,
    HybridCache cache,
    ICurrencyProvider currencyProvider,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ProductService> logger) : IProductService
{
    private readonly AppDbContext _context = context;
    private readonly IFileService _fileService = fileService;
    private readonly HybridCache _cache = cache;
    private readonly ICurrencyProvider _currencyProvider = currencyProvider;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ILogger<ProductService> _logger = logger;
    private const string ProductCacheKeyPrefix = "product";
    private const string ProductTagPrefix = "product-tag";
    private const int CacheDurationSeconds = 600; // 10 minutes

    public async Task<Guid> CreateAsync(CreateProductRequest request)
    {
        var category = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId);

        if (category == null) throw new NotFoundException($"Category with ID {request.CategoryId} not found.");

        bool isCoffee = category.IsCoffeeCategory;

        var baseSlug = SlugGenerator.GenerateSlug(request.Name);
        var uniqueSlug = await GenerateUniqueSlugAsync(baseSlug);

        var product = new Product
        {
            Slug = uniqueSlug,
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            IsActive = true,
            CoffeeProfile = isCoffee ? new ProductCoffeeProfile
            {
                OriginId = request.OriginId
            } : null,
            EquipmentProfile = !isCoffee && request.Specifications != null && request.Specifications.Any() ? new ProductEquipmentProfile
            {
                Specifications = request.Specifications
            } : null
        };

        // Add translations (or use Accept-Language header if not provided)
        if (request.Translations != null && request.Translations.Count > 0)
        {
            foreach (var translation in request.Translations)
            {
                // Normalize language code to 2-char format (e.g., "en-US" ? "en")
                var normalizedLanguageCode = !string.IsNullOrWhiteSpace(translation.LanguageCode)
                    ? translation.LanguageCode.Substring(0, Math.Min(2, translation.LanguageCode.Length)).ToLower()
                    : "en";

                product.Translations.Add(new ProductTranslation
                {
                    LanguageCode = normalizedLanguageCode,
                    Name = translation.Name,
                    Description = translation.Description
                });
            }
        }
        else
        {
            // Fallback: Determine language from Accept-Language header
            // This ensures Name/Description are saved with correct language code
            var languageCode = ExtractLanguageFromRequest();

            product.Translations.Add(new ProductTranslation
            {
                LanguageCode = languageCode,
                Name = request.Name,
                Description = request.Description
            });
        }

        if (isCoffee)
        {
            // Add M2M relationships for RoastLevels
            if (request.RoastLevelIds != null && request.RoastLevelIds.Count > 0)
            {
                // Validate all RoastLevelIds exist
                var existingRoastLevels = await _context.RoastLevels
                .Where(r => request.RoastLevelIds.Contains(r.Id))
                .ToListAsync();

                var missingIds = request.RoastLevelIds.Except(existingRoastLevels.Select(r => r.Id)).ToList();
                if (missingIds.Any())
                {
                    throw new BadRequestException(
                        $"The following roast level IDs do not exist: {string.Join(", ", missingIds)}"
                    );
                }

                foreach (var roastLevel in existingRoastLevels)
                {
                    product.CoffeeProfile.RoastLevels.Add(roastLevel);
                }
            }

            // Add M2M relationships for GrindTypes
            if (request.GrindTypeIds != null && request.GrindTypeIds.Count > 0)
            {
                // Validate all GrindTypeIds exist
                var existingGrindTypes = await _context.GrindTypes
                    .Where(g => request.GrindTypeIds.Contains(g.Id))
                    .ToListAsync();

                var missingIds = request.GrindTypeIds.Except(existingGrindTypes.Select(g => g.Id)).ToList();
                if (missingIds.Any())
                {
                    throw new BadRequestException(
                        $"The following grind type IDs do not exist: {string.Join(", ", missingIds)}"
                    );
                }

                foreach (var grindType in existingGrindTypes)
                {
                    product.CoffeeProfile.GrindTypes.Add(grindType);
                }
            }

            // Add FlavourNotes
            if (request.FlavourNotes != null && request.FlavourNotes.Count > 0)
            {
                foreach (var flavourNote in request.FlavourNotes)
                {
                    // If an existing FlavourNote ID is supplied, attach the tracked DB entity
                    // rather than creating a new one — prevents duplicate translation key violations.
                    if (flavourNote.Id.HasValue && flavourNote.Id.Value != Guid.Empty)
                    {
                        var existingNote = await _context.Set<FlavourNote>().FindAsync(flavourNote.Id.Value);
                        if (existingNote != null)
                        {
                            product.CoffeeProfile!.FlavourNotes.Add(existingNote);
                            continue;
                        }
                    }

                    var note = new FlavourNote
                    {
                        Name = flavourNote.Name,
                        DisplayOrder = flavourNote.DisplayOrder
                    };

                    // Add default English translation
                    note.Translations.Add(new FlavourNoteTranslation
                    {
                        LanguageCode = "en",
                        Name = flavourNote.Name
                    });

                    // Add additional translations if provided (skip duplicates of the default "en" already added)
                    if (flavourNote.Translations != null && flavourNote.Translations.Count > 0)
                    {
                        foreach (var translation in flavourNote.Translations)
                        {
                            // Normalize language code to 2-char format (e.g., "en-US" ? "en")
                            var normalizedLanguageCode = !string.IsNullOrWhiteSpace(translation.LanguageCode)
                                ? translation.LanguageCode.Substring(0, Math.Min(2, translation.LanguageCode.Length)).ToLower()
                                : "en";

                            // Avoid adding a duplicate "en" translation if the default was already added
                            if (note.Translations.Any(t => t.LanguageCode == normalizedLanguageCode))
                                continue;

                            note.Translations.Add(new FlavourNoteTranslation
                            {
                                LanguageCode = normalizedLanguageCode,
                                Name = translation.Name
                            });
                        }
                    }

                    product.CoffeeProfile!.FlavourNotes.Add(note);
                }
            }
        } // closing if (isCoffee)

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // Validate and add Variants
        if (request.Variants != null && request.Variants.Count > 0)
        {
            var productVariants = request.Variants.Select(v =>
            {
                var aedPrice = v.Prices?.FirstOrDefault(p => p.CurrencyCode.ToUpper() == "AED")?.Price ?? 0m;
                return new ProductVariant
                {
                    ProductId = product.Id,
                    Sku = v.Sku,
                    Price = aedPrice,
                    StockQuantity = v.StockQuantity,
                    Options = v.Options?.Select(o => new ProductVariantOption
                    {
                        ProductAttributeValueId = o.ProductAttributeValueId
                    }).ToList() ?? new List<ProductVariantOption>(),
                    Prices = v.Prices?.Where(p => p.CurrencyCode.ToUpper() != "AED").Select(p => new ProductVariantPrice
                    {
                        Currency = Enum.Parse<Currency>(p.CurrencyCode, true),
                        Price = p.Price
                    }).ToList() ?? new List<ProductVariantPrice>()
                };
            }).ToList();

            await _context.ProductVariants.AddRangeAsync(productVariants);
        }

        // Handle images if provided
        if (request.Images != null && request.Images.Count > 0)
        {
            bool isMainSet = false;
            foreach (var imageFile in request.Images)
            {
                var imageUrl = await _fileService.UploadImageAsync(imageFile, "products");

                var productImage = new ProductImage
                {
                    ImageUrl = imageUrl,
                    ProductId = product.Id,
                    IsMain = !isMainSet
                };

                if (!isMainSet)
                    isMainSet = true;

                _context.ProductImages.Add(productImage);
            }
        }

        await _context.SaveChangesAsync();
        return product.Id;
    }

    public async Task<DuneFlame.Application.DTOs.Basket.UpsellRecommendationDto?> GetUpsellRecommendationAsync(decimal gapAmount, List<Guid> excludedProductVariantIds, string currencyCode)
    {
        var targetCurrency = ParseCurrencyCode(currencyCode);
        var languageCode = ExtractLanguageFromRequest();

        var query = _context.ProductVariants
            .Include(pv => pv.Prices)
            .Include(pv => pv.Options)
                .ThenInclude(o => o.ProductAttributeValue)
                    .ThenInclude(av => av.ProductAttribute)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.Translations)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.Images)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.CoffeeProfile)
                    .ThenInclude(cp => cp!.Origin)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.CoffeeProfile)
                    .ThenInclude(cp => cp!.RoastLevels)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.CoffeeProfile)
                    .ThenInclude(cp => cp!.GrindTypes)
            .AsSingleQuery()
            .Where(pv => pv.Product != null && pv.Product.IsActive && pv.StockQuantity > 0);

        if (excludedProductVariantIds != null && excludedProductVariantIds.Any())
        {
            query = query.Where(pv => !excludedProductVariantIds.Contains(pv.Id));
        }

        var bestVariant = await query
            .Where(pv => pv.Price >= gapAmount)
            .OrderBy(pv => pv.Price)
            .FirstOrDefaultAsync();

        if (bestVariant == null)
        {
            bestVariant = await query
                .Where(pv => pv.Price < gapAmount)
                .OrderByDescending(pv => pv.Price)
                .FirstOrDefaultAsync();
        }

        if (bestVariant == null || bestVariant.Product == null)
            return null;

        var name = bestVariant.Product.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.Name
                   ?? bestVariant.Product.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                   ?? "Unknown";

        var image = bestVariant.Product.Images?.OrderByDescending(i => i.IsMain).FirstOrDefault()?.ImageUrl;

        var pricesDict = new Dictionary<string, decimal> { { "AED", bestVariant.Price } };
        if (bestVariant.Prices != null)
        {
            foreach (var p in bestVariant.Prices)
            {
                pricesDict[p.Currency.ToString().ToUpper()] = p.Price;
            }
        }

        var coffeeProfile = bestVariant.Product.CoffeeProfile;
        var isCoffee = coffeeProfile != null;

        return new DuneFlame.Application.DTOs.Basket.UpsellRecommendationDto
        {
            ProductId = bestVariant.ProductId,
            ProductVariantId = bestVariant.Id,
            Name = name,
            Slug = bestVariant.Product.Slug,
            ImageUrl = image,
            Price = bestVariant.Price,
            CurrencyCode = currencyCode,
            WeightLabel = bestVariant.Sku,
            AvailablePrices = pricesDict,
            HasVariants = bestVariant.Options != null && bestVariant.Options.Any(),
            IsCoffee = isCoffee,
            OriginName = coffeeProfile?.Origin?.Name,
            RoastLevelNames = isCoffee ? coffeeProfile!.RoastLevels.Select(r => r.Name).ToList() : null,
            GrindTypeNames = isCoffee ? coffeeProfile!.GrindTypes.Select(g => g.Name).ToList() : null,
            Options = bestVariant.Options?.Select(o => new DuneFlame.Application.DTOs.Product.VariantOptionDto(
                o.ProductAttributeValue?.ProductAttribute?.Name ?? string.Empty,
                o.ProductAttributeValue?.Value ?? string.Empty
            )).ToList(),
        };
    }

    public async Task<ProductResponse> GetByIdAsync(Guid id)
    {
        var currentCurrency = _currencyProvider.GetCurrentCurrency();
        var languageCode = ExtractLanguageFromRequest();
        var cacheKey = $"{ProductCacheKeyPrefix}:id:{id}:{currentCurrency}:{languageCode}";

        var cacheTag = $"{ProductTagPrefix}:{id}";

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async (cancellationToken) =>
            {
                var product = await _context.Products
                    .Include(p => p.Category)
                        .ThenInclude(c => c.Translations)
                    .Include(p => p.Brand)
                    .Include(p => p.Translations)
                    .Include(p => p.Images)
                    .Include(p => p.EquipmentProfile)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Prices)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.ProductAttribute)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.Origin)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.FlavourNotes)
                            .ThenInclude(fn => fn.Translations)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                    ?? throw new NotFoundException($"Product with ID {id} not found.");

                return MapToResponse(product, currentCurrency, languageCode);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(CacheDurationSeconds)
            },
            tags: new[] { cacheTag }
        );

        return response;
    }

    public async Task<ProductResponse> GetBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug cannot be empty.", nameof(slug));

        var currentCurrency = _currencyProvider.GetCurrentCurrency();
        var languageCode = ExtractLanguageFromRequest();
        var cacheKey = $"{ProductCacheKeyPrefix}:slug:{slug}:{currentCurrency}:{languageCode}";

        var cacheTag = $"{ProductTagPrefix}:slug:{slug}";

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async (cancellationToken) =>
            {
                var product = await _context.Products
                    .Include(p => p.Category)
                        .ThenInclude(c => c.Translations)
                    .Include(p => p.Brand)
                    .Include(p => p.Translations)
                    .Include(p => p.Images)
                    .Include(p => p.EquipmentProfile)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Prices)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.ProductAttribute)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.Origin)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.FlavourNotes)
                            .ThenInclude(fn => fn.Translations)
                    .AsSplitQuery()
                    .Where(p => p.IsActive && p.Slug == slug)
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException($"Product with slug '{slug}' not found.");

                return MapToResponse(product, currentCurrency, languageCode);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(CacheDurationSeconds)
            },
            tags: new[] { cacheTag }
        );

        return response;
    }

    public async Task<PagedResult<ProductResponse>> GetAllAsync(
        int pageNumber = 1,
        int pageSize = 8,
        string? sortBy = null,
        string? search = null,
        Guid? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        Guid? brandId = null,
        Guid[]? roastLevelIds = null,
        Guid[]? originIds = null)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 8;
        if (pageSize > 100) pageSize = 100;

        var currentCurrency = _currencyProvider.GetCurrentCurrency();
        var languageCode = ExtractLanguageFromRequest();
        _logger.LogInformation("Shop request for currency: {Currency}, language: {Language}, minPrice: {MinPrice}, maxPrice: {MaxPrice}", currentCurrency, languageCode, minPrice, maxPrice);

        // Build cache key including new filters
        var roastLevelIdsCacheKey = roastLevelIds != null ? string.Join(",", roastLevelIds.OrderBy(x => x)) : "null";
        var originIdsCacheKey = originIds != null ? string.Join(",", originIds.OrderBy(x => x)) : "null";
        var cacheKey = $"{ProductCacheKeyPrefix}:all:{pageNumber}:{pageSize}:{sortBy}:{search}:{categoryId}:{brandId}:{currentCurrency}:{languageCode}:{minPrice}:{maxPrice}:{roastLevelIdsCacheKey}:{originIdsCacheKey}";
        var cacheTag = $"{ProductTagPrefix}:list";

        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            async (cancellationToken) =>
            {
                var query = _context.Products
                    .Include(p => p.Category)
                    .Include(p => p.Brand)
                    .Include(p => p.Translations)
                    .Include(p => p.Images)
                    .Include(p => p.EquipmentProfile)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Prices)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.ProductAttribute)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.Origin)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.FlavourNotes)
                            .ThenInclude(fn => fn.Translations)
                    .AsSplitQuery()
                    .Where(p => p.IsActive)
                    .AsQueryable();

                if (categoryId.HasValue)
                {
                    // Recursively collect the target category + all its descendants
                    // so browsing e.g. "equipment" returns products from "professional-coffee-grinders"
                    var categoryIds = await GetDescendantCategoryIdsAsync(categoryId.Value);
                    query = query.Where(p => categoryIds.Contains(p.CategoryId));
                }

                if (brandId.HasValue)
                {
                    query = query.Where(p => p.BrandId == brandId.Value);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    query = query.Where(p =>
                        p.Translations.Any(t => t.Name.Contains(search)) ||
                        p.Translations.Any(t => t.Description.Contains(search)));
                }

                // ========== NEW: Filter by Roast Levels (BEFORE pagination) ==========
                if (roastLevelIds != null && roastLevelIds.Length > 0)
                {
                    query = query.Where(p =>
                        p.CoffeeProfile != null && p.CoffeeProfile.RoastLevels.Any(r => roastLevelIds.Contains(r.Id)));
                    _logger.LogInformation("Applied roast level filter: {RoastLevelIds}", string.Join(",", roastLevelIds));
                }

                // ========== NEW: Filter by Origins (BEFORE pagination) ==========
                if (originIds != null && originIds.Length > 0)
                {
                    query = query.Where(p =>
                        p.CoffeeProfile != null && p.CoffeeProfile.OriginId.HasValue && originIds.Contains(p.CoffeeProfile.OriginId.Value));
                    _logger.LogInformation("Applied origin filter: {OriginIds}", string.Join(",", originIds));
                }

                // ========== NEW: Extended Sorting Support ==========
                query = sortBy?.ToLower() switch
                {
                    "stock-asc" => query.OrderBy(p => p.Variants.Sum(v => v.StockQuantity)),
                    "stock-desc" => query.OrderByDescending(p => p.Variants.Sum(v => v.StockQuantity)),
                    "date-asc" => query.OrderBy(p => p.CreatedAt),
                    "date-desc" => query.OrderByDescending(p => p.CreatedAt),
                    "name-asc" => query.OrderBy(p => p.Translations.FirstOrDefault(t => t.LanguageCode == "en").Name),
                    "name-desc" => query.OrderByDescending(p => p.Translations.FirstOrDefault(t => t.LanguageCode == "en").Name),
                    "price-asc" => query.OrderBy(p => p.Variants.FirstOrDefault().Price),
                    "price-desc" => query.OrderByDescending(p => p.Variants.FirstOrDefault().Price),
                    _ => query.OrderByDescending(p => p.CreatedAt)
                };

                // Apply price filter only if a specific range is requested (not the default 0-100)
                // This allows all products to load initially on the Shop page
                if ((minPrice.HasValue && minPrice.Value > 0) || (maxPrice.HasValue && maxPrice.Value < 10000))
                {
                    var min = minPrice ?? 0;
                    var max = maxPrice ?? decimal.MaxValue;
                    query = query.Where(p => p.Variants.Any(v =>
                        v.Price >= min &&
                        v.Price <= max));

                    _logger.LogInformation("Applied price filter: {MinPrice} - {MaxPrice} for currency {Currency}", min, max, currentCurrency);
                }
                else
                {
                    _logger.LogInformation("Skipping price filter (default range). Loading all products for currency {Currency}", currentCurrency);
                }

                var totalCount = await query.CountAsync(cancellationToken);
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var products = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(cancellationToken);

                var responses = products.Select(p => MapToResponse(p, currentCurrency, languageCode)).ToList();

                return new PagedResult<ProductResponse>(
                    responses,
                    totalCount,
                    pageNumber,
                    pageSize,
                    totalPages
                );
            },
                    new HybridCacheEntryOptions
                    {
                        Expiration = TimeSpan.FromSeconds(1) // Reduced to 1 second to bypass stale cache during testing
                    },
                    tags: new[] { cacheTag }
                );

        return result;
    }

    public async Task<PagedResult<ProductResponse>> GetAllAdminAsync(
                        int pageNumber = 1,
                        int pageSize = 10,
                        string? sortBy = null,
                        string? search = null,
                        Guid? categoryId = null,
                        decimal? minPrice = null,
                        decimal? maxPrice = null,
                        Guid? brandId = null,
                        Guid[]? roastLevelIds = null,
                        Guid[]? originIds = null)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var currentCurrency = _currencyProvider.GetCurrentCurrency();
        var languageCode = ExtractLanguageFromRequest();

        var roastLevelIdsCacheKey = roastLevelIds != null ? string.Join(",", roastLevelIds.OrderBy(x => x)) : "null";
        var originIdsCacheKey = originIds != null ? string.Join(",", originIds.OrderBy(x => x)) : "null";
        var cacheKey = $"{ProductCacheKeyPrefix}:admin:{pageNumber}:{pageSize}:{sortBy}:{search}:{categoryId}:{brandId}:{currentCurrency}:{languageCode}:{minPrice}:{maxPrice}:{roastLevelIdsCacheKey}:{originIdsCacheKey}";
        var cacheTag = $"{ProductTagPrefix}:admin-list";

        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            async (cancellationToken) =>
            {
                var query = _context.Products
                    .Include(p => p.Category)
                        .ThenInclude(c => c.Translations)
                    .Include(p => p.Brand)
                    .Include(p => p.Translations)
                    .Include(p => p.Images)
                    .Include(p => p.EquipmentProfile)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Prices)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.ProductAttribute)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.Origin)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.FlavourNotes)
                            .ThenInclude(fn => fn.Translations)
                    .AsSplitQuery()
                    .AsQueryable();

                if (categoryId.HasValue)
                {
                    // Recursively collect the target category + all its descendants
                    var categoryIds = await GetDescendantCategoryIdsAsync(categoryId.Value);
                    query = query.Where(p => categoryIds.Contains(p.CategoryId));
                }

                if (brandId.HasValue)
                {
                    query = query.Where(p => p.BrandId == brandId.Value);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    query = query.Where(p =>
                        p.Translations.Any(t => t.Name.Contains(search)) ||
                        p.Translations.Any(t => t.Description.Contains(search)));
                }

                // Filter by Roast Levels (BEFORE pagination)
                if (roastLevelIds != null && roastLevelIds.Length > 0)
                {
                    query = query.Where(p =>
                        p.CoffeeProfile != null && p.CoffeeProfile.RoastLevels.Any(r => roastLevelIds.Contains(r.Id)));
                }

                // Filter by Origins (BEFORE pagination)
                if (originIds != null && originIds.Length > 0)
                {
                    query = query.Where(p =>
                        p.CoffeeProfile != null && p.CoffeeProfile.OriginId.HasValue && originIds.Contains(p.CoffeeProfile.OriginId.Value));
                }

                // Extended Sorting Support
                query = sortBy?.ToLower() switch
                {
                    "stock-asc" => query.OrderBy(p => p.Variants.Sum(v => v.StockQuantity)),
                    "stock-desc" => query.OrderByDescending(p => p.Variants.Sum(v => v.StockQuantity)),
                    "date-asc" => query.OrderBy(p => p.CreatedAt),
                    "date-desc" => query.OrderByDescending(p => p.CreatedAt),
                    "name-asc" => query.OrderBy(p => p.Translations.FirstOrDefault(t => t.LanguageCode == "en").Name),
                    "name-desc" => query.OrderByDescending(p => p.Translations.FirstOrDefault(t => t.LanguageCode == "en").Name),
                    "price-asc" => query.OrderBy(p => p.Variants.FirstOrDefault().Price),
                    "price-desc" => query.OrderByDescending(p => p.Variants.FirstOrDefault().Price),
                    _ => query.OrderByDescending(p => p.CreatedAt)
                };

                // Apply price filter only if a specific range is requested (not the default 0-100)
                // This allows all products to load initially on the admin page
                if ((minPrice.HasValue && minPrice.Value > 0) || (maxPrice.HasValue && maxPrice.Value < 10000))
                {
                    var min = minPrice ?? 0;
                    var max = maxPrice ?? decimal.MaxValue;
                    query = query.Where(p => p.Variants.Any(v =>
                        v.Price >= min &&
                        v.Price <= max));

                    _logger.LogInformation("Applied price filter (admin): {MinPrice} - {MaxPrice} for currency {Currency}", min, max, currentCurrency);
                }
                else
                {
                    _logger.LogInformation("Skipping price filter (admin, default range). Loading all products for currency {Currency}", currentCurrency);
                }

                var totalCount = await query.CountAsync(cancellationToken);
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var products = await query
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(cancellationToken);

                var responses = products.Select(p => MapToResponse(p, currentCurrency, languageCode)).ToList();

                return new PagedResult<ProductResponse>(
                    responses,
                    totalCount,
                    pageNumber,
                    pageSize,
                    totalPages
                );
            },
                    new HybridCacheEntryOptions
                    {
                        Expiration = TimeSpan.FromSeconds(1) // Reduced to 1 second to bypass stale cache during testing
                    },
                    tags: new[] { cacheTag }
                );

        return result;
    }

    // -----------------------------------------------------------------
    // UTILITY
    // -----------------------------------------------------------------
    private static string NormalizeLang(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return "en";
        return languageCode.Substring(0, Math.Min(2, languageCode.Length)).ToLower();
    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await _context.Products
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException($"Product with ID {id} not found.");

        // Check if product has any existing orders
        bool hasOrders = await _context.OrderItems
            .AnyAsync(oi => product.Variants.Select(p => p.Id).Contains(oi.ProductVariantId));

        if (hasOrders)
        {
            // Soft Delete: Product is referenced by orders, cannot hard delete
            // Preserve product and images for order history integrity
            product.IsActive = false;
            _logger.LogInformation(
                "Product {ProductId} soft-deleted because it has associated orders. Product remains in database with IsActive=false for order history",
                id);
        }
        else
        {
            // Hard Delete: No orders reference this product, safe to completely remove
            foreach (var image in product.Images)
            {
                _fileService.DeleteFile(image.ImageUrl);
            }

            _context.Products.Remove(product);
            _logger.LogInformation(
                "Product {ProductId} hard-deleted: removed from database and deleted all associated images",
                id);
        }

        await _context.SaveChangesAsync();

        // Invalidate all caches for this product
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:{id}");
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:slug:{product.Slug}");
    }

    public async Task RestoreAsync(Guid id)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException($"Product with ID {id} not found.");

        // Restore the product by setting IsActive back to true
        product.IsActive = true;
        _logger.LogInformation(
            "Product {ProductId} restored. IsActive set to true",
            id);

        await _context.SaveChangesAsync();

        // Invalidate all caches for this product
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:{id}");
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:slug:{product.Slug}");
    }

    private static ProductResponse MapToResponse(Product product, Currency currentCurrency, string languageCode)
    {
        // 1. Resolve Translations safely
        var translation = product.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)
                          ?? product.Translations?.FirstOrDefault(t => t.LanguageCode == "en")
                          ?? product.Translations?.FirstOrDefault();

        string productName = translation?.Name ?? $"[No Name - {product.Slug}]";
        string productDesc = translation?.Description ?? "No description available";

        // 2. Map Coffee Profile
        ProductCoffeeProfileDto? coffeeProfileDto = null;
        if (product.CoffeeProfile != null)
        {
            coffeeProfileDto = new ProductCoffeeProfileDto(
                OriginId: product.CoffeeProfile.OriginId,
                OriginName: product.CoffeeProfile.Origin?.Name,
                RoastLevelNames: product.CoffeeProfile.RoastLevels?.Select(r => r.Name).ToList() ?? new List<string>(),
                GrindTypeNames: product.CoffeeProfile.GrindTypes?.Select(g => g.Name).ToList() ?? new List<string>(),
                RoastLevelIds: product.CoffeeProfile.RoastLevels?.Select(r => r.Id).ToList() ?? new List<Guid>(),
                GrindTypeIds: product.CoffeeProfile.GrindTypes?.Select(g => g.Id).ToList() ?? new List<Guid>(),
                FlavourNotes: product.CoffeeProfile.FlavourNotes?.OrderBy(fn => fn.DisplayOrder).Select(fn =>
                {
                    var fnTranslation = fn.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)
                                        ?? fn.Translations?.FirstOrDefault(t => t.LanguageCode == "en");

                    return new FlavourNoteDto(
                        Id: fn.Id,
                        Name: fnTranslation?.Name ?? fn.Name,
                        DisplayOrder: fn.DisplayOrder,
                        Translations: fn.Translations?.Select(t => new FlavourNoteTranslationDto(
                            FlavourNoteId: fn.Id,
                            LanguageCode: t.LanguageCode,
                            Name: t.Name
                        )).ToList() ?? new List<FlavourNoteTranslationDto>()
                    );
                }).ToList() ?? new List<FlavourNoteDto>()
            );
        }

        // 3. Map Variants and Prices
        var variants = product.Variants?.DistinctBy(v => v.Id).Select(v =>
        {
            // Base price fallback logic
            var activePrice = v.Prices?.FirstOrDefault(p => p.Currency == currentCurrency)?.Price ?? v.Price;

            // Map Prices collection (include base AED + overrides)
            var mappedPrices = v.Prices?.Select(p => new VariantPriceDto(p.Currency.ToString(), p.Price)).ToList() ?? new List<VariantPriceDto>();
            // Ensure AED is always in the list for the admin UI
            if (!mappedPrices.Any(p => p.CurrencyCode == "AED"))
            {
                mappedPrices.Add(new VariantPriceDto("AED", v.Price));
            }

            return new VariantDto(
                Id: v.Id,
                Sku: v.Sku,
                Price: activePrice,
                StockQuantity: v.StockQuantity,
                Options: v.Options?.Select(o => new VariantOptionDto(
                    AttributeName: o.ProductAttributeValue?.ProductAttribute?.Name ?? string.Empty,
                    Value: o.ProductAttributeValue?.Value ?? string.Empty
                )).ToList() ?? new List<VariantOptionDto>(),
                Prices: mappedPrices
            );
        }).ToList() ?? new List<VariantDto>();

        return new ProductResponse(
            Id: product.Id,
            Name: productName,
            Slug: product.Slug,
            Description: productDesc,
            IsActive: product.IsActive,
            CategoryId: product.CategoryId,
            CategoryName: product.Category?.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.Name
                          ?? product.Category?.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                          ?? "Unknown",
            BrandId: product.BrandId,
            BrandName: product.Brand?.Name,
            Translations: product.Translations?.Select(t => new ProductTranslationDto(
                 LanguageCode: t.LanguageCode,
                 Name: t.Name,
                 Description: t.Description
            )).ToList() ?? new List<ProductTranslationDto>(),
            CoffeeProfile: coffeeProfileDto,
            Specifications: product.EquipmentProfile?.Specifications,
            Variants: variants,
            CreatedAt: product.CreatedAt,
            UpdatedAt: product.UpdatedAt,
            Images: product.Images?.OrderByDescending(i => i.IsMain).Select(i => new ProductImageDto(
                Id: i.Id,
                ImageUrl: i.ImageUrl,
                IsMain: i.IsMain
            )).ToList() ?? new List<ProductImageDto>()
        );
    }

    private async Task<string> GenerateUniqueSlugAsync(string baseSlug, Guid? excludeProductId = null)
    {
        var slug = baseSlug;
        var counter = 1;

        while (await _context.Products.AnyAsync(p => p.Slug == slug && p.Id != excludeProductId))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }

        return slug;
    }

    /// <summary>
    /// Extracts the language code from the Accept-Language header.
    /// Defaults to "en" if not specified or unsupported.
    /// <summary>
    /// Returns the target category's own ID plus all descendant category IDs
    /// using an in-memory BFS over the full category list fetched in one query.
    /// This avoids N+1 recursive DB calls while supporting arbitrary tree depth.
    /// </summary>
    private async Task<HashSet<Guid>> GetDescendantCategoryIdsAsync(Guid rootCategoryId)
    {
        var all = await _context.Categories
            .AsNoTracking()
            .Select(c => new { c.Id, c.ParentCategoryId })
            .ToListAsync();

        var byParent = all.GroupBy(c => c.ParentCategoryId)
                          .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

        var result = new HashSet<Guid> { rootCategoryId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootCategoryId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!byParent.TryGetValue(current, out var children)) continue;
            foreach (var child in children)
            {
                if (result.Add(child))
                    queue.Enqueue(child);
            }
        }

        return result;
    }

    /// <summary>
    /// Extracts the preferred language from the Accept-Language request header.
    /// Supports: "ar" (Arabic) and "en" (English).
    /// </summary>
    private string ExtractLanguageFromRequest()
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return "en"; // Default
            }

            // Read Accept-Language header
            var acceptLanguage = httpContext.Request.Headers["Accept-Language"].ToString();

            if (string.IsNullOrWhiteSpace(acceptLanguage))
            {
                return "en"; // Default
            }

            // Extract primary language (e.g., "ar-AE" or "ar" -> "ar")
            var langPart = acceptLanguage.Split(',')[0].Trim();
            var lang = langPart.Length >= 2 ? langPart.Substring(0, 2).ToLower() : "en";

            // Support only "ar" and "en"
            return lang switch
            {
                "ar" => "ar",
                _ => "en"
            };
        }
        catch
        {
            return "en"; // Default on any error
        }
    }

    /// <summary>
    /// Parses currency code string to Currency enum.
    /// Defaults to USD if invalid code is provided.
    /// </summary>
    private Currency ParseCurrencyCode(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            return Currency.USD;

        return Enum.TryParse<Currency>(currencyCode.Trim().ToUpper(), out var currency)
            ? currency
            : Currency.USD;
    }
}
