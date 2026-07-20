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
    ILogger<ProductService> logger,
    IGoogleMerchantService merchantService,
    IEnumerable<DuneFlame.Infrastructure.Products.Commands.UpdateProduct.Strategies.IProductUpdateStrategy> updateStrategies) : IProductService
{
    private readonly AppDbContext _context = context;
    private readonly IFileService _fileService = fileService;
    private readonly HybridCache _cache = cache;
    private readonly ICurrencyProvider _currencyProvider = currencyProvider;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ILogger<ProductService> _logger = logger;
    private readonly IGoogleMerchantService _merchantService = merchantService;
    private readonly IEnumerable<DuneFlame.Infrastructure.Products.Commands.UpdateProduct.Strategies.IProductUpdateStrategy> _updateStrategies = updateStrategies;
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
                // Normalize language code to 2-char format (e.g., "en-US" → "en")
                var normalizedLanguageCode = !string.IsNullOrWhiteSpace(translation.LanguageCode)
                    ? translation.LanguageCode.Substring(0, Math.Min(2, translation.LanguageCode.Length)).ToLower()
                    : "en";

                product.Translations.Add(new ProductTranslation
                {
                    LanguageCode = normalizedLanguageCode,
                    Name = translation.Name,
                    Description = translation.Description,
                    // Auto-generate SEO fields when the admin hasn't provided them
                    MetaTitle = translation.MetaTitle
                              ?? SeoGenerator.GenerateMetaTitle(translation.Name, normalizedLanguageCode),
                    MetaDescription = translation.MetaDescription
                                   ?? SeoGenerator.GenerateMetaDescription(translation.Description)
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
                Description = request.Description,
                // Auto-generate SEO fields (no DTO equivalent in the single-language path)
                MetaTitle = SeoGenerator.GenerateMetaTitle(request.Name, languageCode),
                MetaDescription = SeoGenerator.GenerateMetaDescription(request.Description)
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
            // Use the English product name for alt text (best for Google Image indexing)
            var enName = product.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                         ?? product.Translations.FirstOrDefault()?.Name
                         ?? product.Slug;

            bool isMainSet = false;
            foreach (var imageFile in request.Images)
            {
                var imageUrl = await _fileService.UploadImageAsync(imageFile, "products");

                var productImage = new ProductImage
                {
                    ImageUrl = imageUrl,
                    ProductId = product.Id,
                    IsMain = !isMainSet,
                    // Auto-generate AltText — critical for Google Image indexing
                    AltText = SeoGenerator.GenerateAltText(enName)
                };

                if (!isMainSet)
                    isMainSet = true;

                _context.ProductImages.Add(productImage);
            }
        }

        await _context.SaveChangesAsync();

        // --- Fire Merchant Center sync (non-blocking — failures are logged, never thrown) ---
        // Re-load the product with all navigations so the mapper has complete data
        var syncProduct = await _context.Products
            .Include(p => p.Translations)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == product.Id);
        if (syncProduct != null)
            await _merchantService.SyncProductToMerchantCenterAsync(syncProduct);

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
                    .ThenInclude(av => av.Translations)
            .Include(pv => pv.Options)
                .ThenInclude(o => o.ProductAttributeValue)
                    .ThenInclude(av => av.ProductAttribute)
                        .ThenInclude(a => a.Translations)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.Translations)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.Images)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.CoffeeProfile)
                    .ThenInclude(cp => cp!.Origin)
                        .ThenInclude(o => o!.Translations)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.CoffeeProfile)
                    .ThenInclude(cp => cp!.RoastLevels)
                        .ThenInclude(r => r.Translations)
            .Include(pv => pv.Product)
                .ThenInclude(p => p.CoffeeProfile)
                    .ThenInclude(cp => cp!.GrindTypes)
                        .ThenInclude(g => g.Translations)
            .AsSingleQuery()
            .Where(pv => pv.Product != null && !pv.Product.IsDeleted && pv.StockQuantity > 0);

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

        var upsellOriginName = coffeeProfile?.Origin?.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedName
                            ?? coffeeProfile?.Origin?.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                            ?? coffeeProfile?.Origin?.Name;

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
            OriginName = upsellOriginName,
            RoastLevelNames = isCoffee ? coffeeProfile!.RoastLevels.Select(r =>
                r.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedName
                ?? r.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                ?? r.Name).ToList() : null,
            GrindTypeNames = isCoffee ? coffeeProfile!.GrindTypes.Select(g =>
                g.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedName
                ?? g.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                ?? g.Name).ToList() : null,
            Options = bestVariant.Options?.Select(o =>
            {
                var attrName = o.ProductAttributeValue?.ProductAttribute?.Translations
                    ?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedName
                    ?? o.ProductAttributeValue?.ProductAttribute?.Translations
                    ?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                    ?? o.ProductAttributeValue?.ProductAttribute?.Name
                    ?? string.Empty;

                var attrValue = o.ProductAttributeValue?.Translations
                    ?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedValue
                    ?? o.ProductAttributeValue?.Translations
                    ?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedValue
                    ?? o.ProductAttributeValue?.Value
                    ?? string.Empty;

                return new DuneFlame.Application.DTOs.Product.VariantOptionDto(attrName, attrValue);
            }).ToList(),
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
                        .ThenInclude(b => b!.Translations)
                    .Include(p => p.Translations)
                    .Include(p => p.Images)
                    .Include(p => p.EquipmentProfile)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Prices)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.Translations)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.ProductAttribute)
                                    .ThenInclude(a => a.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.Origin)
                            .ThenInclude(o => o!.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                            .ThenInclude(r => r.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                            .ThenInclude(g => g.Translations)
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

        // --- Step 1: Try the normal active-product cache path ---
        // We must execute the slug-history check OUTSIDE the cache factory because
        // HybridCache factories must not throw domain-routing exceptions.
        var activeProduct = await _context.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.Slug == slug)
            .Select(p => p.Id)   // cheap scalar check — no heavy includes yet
            .FirstOrDefaultAsync();

        if (activeProduct == Guid.Empty)
        {
            // --- Step 2: Active product not found — check slug history for a 301 redirect ---
            var history = await _context.ProductSlugHistories
                .AsNoTracking()
                .Where(h => h.OldSlug == slug)
                .Select(h => new { h.Product!.Slug, h.Product.IsDeleted })
                .FirstOrDefaultAsync();

            if (history != null && !history.IsDeleted)
                throw new ProductMovedPermanentlyException(slug, history.Slug);

            // Genuinely gone — history record exists for an inactive product, or never existed
            throw new NotFoundException($"Product with slug '{slug}' not found.");
        }

        // --- Step 3: Active product confirmed — load full object graph via cache ---
        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async (cancellationToken) =>
            {
                var product = await _context.Products
                    .Include(p => p.Category)
                        .ThenInclude(c => c.Translations)
                    .Include(p => p.Brand)
                        .ThenInclude(b => b!.Translations)
                    .Include(p => p.Translations)
                    .Include(p => p.Images)
                    .Include(p => p.EquipmentProfile)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Prices)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.Translations)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.ProductAttribute)
                                    .ThenInclude(a => a.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.Origin)
                            .ThenInclude(o => o!.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                            .ThenInclude(r => r.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                            .ThenInclude(g => g.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.FlavourNotes)
                            .ThenInclude(fn => fn.Translations)
                    .AsSplitQuery()
                    .Where(p => !p.IsDeleted && p.Slug == slug)
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
                        .ThenInclude(c => c.Translations)
                    .Include(p => p.Brand)
                        .ThenInclude(b => b!.Translations)
                    .Include(p => p.Translations)
                    .Include(p => p.Images)
                    .Include(p => p.EquipmentProfile)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Prices)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.Translations)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.ProductAttribute)
                                    .ThenInclude(a => a.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.Origin)
                            .ThenInclude(o => o!.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                            .ThenInclude(r => r.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                            .ThenInclude(g => g.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.FlavourNotes)
                            .ThenInclude(fn => fn.Translations)
                    .AsSplitQuery()
                    .Where(p => !p.IsDeleted)
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
                    // Sort by the minimum currency-aware price across all variants.
                    // Uses ProductVariantPrice (per-currency override) cast to decimal? so
                    // FirstOrDefault() returns null (not 0) when no override row exists,
                    // then ?? falls back to the base ProductVariant.Price (AED).
                    // This pattern is fully translatable by EF Core → SQL COALESCE.
                    "price-asc" => query.OrderBy(p => p.Variants.Min(v =>
                        v.Prices
                            .Where(pr => pr.Currency == currentCurrency)
                            .Select(pr => (decimal?)pr.Price)
                            .FirstOrDefault() ?? v.Price)),
                    "price-desc" => query.OrderByDescending(p => p.Variants.Min(v =>
                        v.Prices
                            .Where(pr => pr.Currency == currentCurrency)
                            .Select(pr => (decimal?)pr.Price)
                            .FirstOrDefault() ?? v.Price)),
                    _ => query.OrderByDescending(p => p.CreatedAt)
                };

                // Apply price filter only if a specific range is requested (not the default 0–10000).
                // Each bound is guarded separately so either can be omitted independently.
                // The currency-aware price expression mirrors the sort key exactly:
                //   (decimal?)pr.Price gives null (not 0) when no override row exists,
                //   ?? v.Price falls back to the base AED price — translates to SQL COALESCE.
                if (minPrice.HasValue && minPrice.Value > 0)
                {
                    query = query.Where(p => p.Variants.Any(v =>
                        (v.Prices
                            .Where(pr => pr.Currency == currentCurrency)
                            .Select(pr => (decimal?)pr.Price)
                            .FirstOrDefault() ?? v.Price) >= minPrice.Value));

                    _logger.LogInformation("Applied min price filter: {MinPrice} for currency {Currency}", minPrice.Value, currentCurrency);
                }

                if (maxPrice.HasValue && maxPrice.Value < 10000)
                {
                    query = query.Where(p => p.Variants.Any(v =>
                        (v.Prices
                            .Where(pr => pr.Currency == currentCurrency)
                            .Select(pr => (decimal?)pr.Price)
                            .FirstOrDefault() ?? v.Price) <= maxPrice.Value));

                    _logger.LogInformation("Applied max price filter: {MaxPrice} for currency {Currency}", maxPrice.Value, currentCurrency);
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
                        Expiration = TimeSpan.FromSeconds(CacheDurationSeconds) // 10 minutes, consistent with single-product TTL
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
                        .ThenInclude(b => b!.Translations)
                    .Include(p => p.Translations)
                    .Include(p => p.Images)
                    .Include(p => p.EquipmentProfile)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Prices)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.Translations)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Options)
                            .ThenInclude(o => o.ProductAttributeValue)
                                .ThenInclude(av => av.ProductAttribute)
                                    .ThenInclude(a => a.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.Origin)
                            .ThenInclude(o => o!.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.RoastLevels)
                            .ThenInclude(r => r.Translations)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                    .Include(p => p.CoffeeProfile)
                        .ThenInclude(cp => cp.GrindTypes)
                            .ThenInclude(g => g.Translations)
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
                        Expiration = TimeSpan.FromSeconds(CacheDurationSeconds) // 10 minutes, consistent with single-product TTL
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

    public async Task<bool> UpdateAsync(DuneFlame.Application.Products.Commands.UpdateProduct.UpdateProductCommand request)
    {
        _logger.LogInformation("Starting UpdateAsync for Product ID: {Id}", request.Id);
        _logger.LogInformation("Incoming Translation Langs: {Langs}", string.Join(",", request.Translations?.Select(t => t.LanguageCode) ?? Array.Empty<string>()));
        _logger.LogInformation("Incoming Variant IDs: {Ids}", string.Join(",", request.Variants?.Select(v => v.Id?.ToString() ?? "null") ?? Array.Empty<string>()));

        if (!string.IsNullOrWhiteSpace(request.SpecificationsJson))
        {
            try
            {
                request.Specifications = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(request.SpecificationsJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize SpecificationsJson: {Json}", request.SpecificationsJson);
                throw new ArgumentException("Invalid Specifications JSON format.");
            }
        }

        var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Translations)
            .Include(p => p.Images)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Prices)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Options)
            .Include(p => p.CoffeeProfile)
                .ThenInclude(cp => cp.RoastLevels)
            .Include(p => p.CoffeeProfile)
                .ThenInclude(cp => cp.GrindTypes)
            .Include(p => p.CoffeeProfile)
                .ThenInclude(cp => cp.FlavourNotes)
                    .ThenInclude(fn => fn.Translations)
            .Include(p => p.EquipmentProfile)
            .FirstOrDefaultAsync(p => p.Id == request.Id);

        if (product == null)
            throw new KeyNotFoundException($"Product with ID {request.Id} not found.");

        // --- Slug change detection: capture current slug BEFORE any mutations ---
        var oldSlug = product.Slug;

        if (product.CategoryId != request.CategoryId) product.CategoryId = request.CategoryId;
        if (product.BrandId != request.BrandId) product.BrandId = request.BrandId;

        if (request.Translations != null)
        {
            var existingTranslations = product.Translations.ToList();
            var orphanedTranslations = existingTranslations
                .Where(e => !request.Translations.Any(r => r.LanguageCode == e.LanguageCode))
                .ToList();

            if (orphanedTranslations.Any())
                _context.ProductTranslations.RemoveRange(orphanedTranslations);

            foreach (var tDto in request.Translations)
            {
                var existingTrans = product.Translations.FirstOrDefault(e => e.LanguageCode == tDto.LanguageCode);
                if (existingTrans != null)
                {
                    if (existingTrans.Name != tDto.Name) existingTrans.Name = tDto.Name;
                    if (existingTrans.Description != tDto.Description) existingTrans.Description = tDto.Description;
                    // SEO fields: explicit admin value wins; auto-generate only when still null
                    existingTrans.MetaTitle = tDto.MetaTitle
                        ?? existingTrans.MetaTitle
                        ?? SeoGenerator.GenerateMetaTitle(existingTrans.Name, existingTrans.LanguageCode);
                    existingTrans.MetaDescription = tDto.MetaDescription
                        ?? existingTrans.MetaDescription
                        ?? SeoGenerator.GenerateMetaDescription(existingTrans.Description);
                }
                else
                {
                    product.Translations.Add(new ProductTranslation
                    {
                        ProductId = product.Id,
                        LanguageCode = tDto.LanguageCode,
                        Name = tDto.Name,
                        Description = tDto.Description,
                        MetaTitle = tDto.MetaTitle
                                 ?? SeoGenerator.GenerateMetaTitle(tDto.Name, tDto.LanguageCode),
                        MetaDescription = tDto.MetaDescription
                                       ?? SeoGenerator.GenerateMetaDescription(tDto.Description)
                    });
                }
            }
        }
        else
        {
            var existingEn = product.Translations.FirstOrDefault(e => e.LanguageCode == "en");
            if (existingEn != null)
            {
                if (existingEn.Name != request.Name) existingEn.Name = request.Name;
                if (existingEn.Description != request.Description) existingEn.Description = request.Description;
                // Auto-generate SEO fields from the (potentially updated) name/description when not yet set
                existingEn.MetaTitle ??= SeoGenerator.GenerateMetaTitle(existingEn.Name, "en");
                existingEn.MetaDescription ??= SeoGenerator.GenerateMetaDescription(existingEn.Description);
            }
            else
            {
                product.Translations.Add(new ProductTranslation
                {
                    LanguageCode = "en",
                    Name = request.Name,
                    Description = request.Description,
                    MetaTitle = SeoGenerator.GenerateMetaTitle(request.Name, "en"),
                    MetaDescription = SeoGenerator.GenerateMetaDescription(request.Description)
                });
            }
        }

        var existingVariants = product.Variants.ToList();
        var orphanedVariants = existingVariants.Where(e => !request.Variants.Any(r => r.Id == e.Id)).ToList();
        if (orphanedVariants.Any())
        {
            _context.RemoveRange(orphanedVariants.SelectMany(v => v.Prices));
            _context.RemoveRange(orphanedVariants.SelectMany(v => v.Options));
            _context.RemoveRange(orphanedVariants);
        }

        foreach (var vDto in request.Variants)
        {
            if (vDto.Id.HasValue && vDto.Id.Value != Guid.Empty)
            {
                var existingVar = existingVariants.FirstOrDefault(e => e.Id == vDto.Id);
                if (existingVar != null)
                {
                    if (existingVar.Sku != vDto.Sku) existingVar.Sku = vDto.Sku;
                    if (existingVar.StockQuantity != vDto.StockQuantity) existingVar.StockQuantity = vDto.StockQuantity;

                    var existingOptions = existingVar.Options.ToList();
                    var orphanedOptions = existingOptions.Where(e => !vDto.Options.Any(o => o.ProductAttributeValueId == e.ProductAttributeValueId)).ToList();
                    if (orphanedOptions.Any()) _context.RemoveRange(orphanedOptions);

                    foreach (var opt in vDto.Options)
                    {
                        if (!existingOptions.Any(e => e.ProductAttributeValueId == opt.ProductAttributeValueId))
                        {
                            var newOpt = new ProductVariantOption
                            {
                                ProductVariantId = existingVar.Id,
                                ProductAttributeValueId = opt.ProductAttributeValueId
                            };
                            _context.Entry(newOpt).State = EntityState.Added;
                            existingVar.Options.Add(newOpt);
                        }
                    }

                    if (vDto.Prices != null)
                    {
                        var existingPrices = existingVar.Prices.ToList();
                        var orphanedPrices = existingPrices.Where(e => !vDto.Prices.Any(p => Enum.TryParse<Currency>(p.CurrencyCode, true, out var c) && c == e.Currency)).ToList();
                        if (orphanedPrices.Any()) _context.RemoveRange(orphanedPrices);

                        foreach (var priceDto in vDto.Prices)
                        {
                            if (Enum.TryParse<Currency>(priceDto.CurrencyCode, true, out var currencyCode))
                            {
                                var existingPrice = existingPrices.FirstOrDefault(e => e.Currency == currencyCode);
                                if (existingPrice != null)
                                {
                                    if (existingPrice.Price != priceDto.Price) existingPrice.Price = priceDto.Price;
                                }
                                else
                                {
                                    var newPrice = new ProductVariantPrice
                                    {
                                        ProductVariantId = existingVar.Id,
                                        Currency = currencyCode,
                                        Price = priceDto.Price
                                    };
                                    _context.Entry(newPrice).State = EntityState.Added;
                                    existingVar.Prices.Add(newPrice);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                var newVar = new ProductVariant
                {
                    Sku = vDto.Sku,
                    StockQuantity = vDto.StockQuantity,
                    Options = vDto.Options?.Select(o => new ProductVariantOption { ProductAttributeValueId = o.ProductAttributeValueId }).ToList() ?? new(),
                    Prices = vDto.Prices?.Select(p => new ProductVariantPrice
                    {
                        Currency = Enum.Parse<Currency>(p.CurrencyCode, true),
                        Price = p.Price
                    }).ToList() ?? new()
                };
                _context.Entry(newVar).State = EntityState.Added;
                foreach (var opt in newVar.Options) _context.Entry(opt).State = EntityState.Added;
                foreach (var price in newVar.Prices) _context.Entry(price).State = EntityState.Added;
                product.Variants.Add(newVar);
            }
        }

        if (request.DeletedImageIds != null && request.DeletedImageIds.Any())
        {
            var imagesToDelete = product.Images.Where(i => request.DeletedImageIds.Contains(i.Id)).ToList();
            foreach (var img in imagesToDelete)
            {
                _fileService.DeleteFile(img.ImageUrl);
                product.Images.Remove(img);
            }
        }

        if (request.SetMainImageId.HasValue)
        {
            foreach (var img in product.Images.Where(i => i.Id != Guid.Empty).ToList())
            {
                var shouldBeMain = img.Id == request.SetMainImageId.Value;
                if (img.IsMain != shouldBeMain) img.IsMain = shouldBeMain;
            }
        }

        if (request.Images != null && request.Images.Any())
        {
            var hasMainImage = product.Images.Any(i => i.IsMain);
            // Resolve English name for alt text — same strategy as CreateAsync
            var enNameForAlt = product.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                               ?? product.Translations.FirstOrDefault()?.Name
                               ?? product.Slug;

            for (int i = 0; i < request.Images.Count; i++)
            {
                var fileUrl = await _fileService.UploadImageAsync(request.Images[i], "products");
                var newImage = new ProductImage
                {
                    ProductId = product.Id,
                    ImageUrl = fileUrl,
                    IsMain = !hasMainImage && i == 0,
                    AltText = SeoGenerator.GenerateAltText(enNameForAlt)
                };
                _context.Entry(newImage).State = EntityState.Added;
                product.Images.Add(newImage);
            }
        }

        var strategy = _updateStrategies.FirstOrDefault(s => s.CanHandle(product.Category));
        if (strategy != null)
            await strategy.ApplyUpdateAsync(product, request, _context);

        // --- Slug change: regenerate and record history within the same SaveChanges ---
        // Slug is derived from the English name. Re-generate it if the English name changed.
        var newEnglishName = request.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                             ?? request.Name;  // fallback for non-translation requests
        var candidateSlug = DuneFlame.Application.Common.SlugGenerator.GenerateSlug(newEnglishName);

        if (!string.IsNullOrWhiteSpace(candidateSlug) && candidateSlug != oldSlug)
        {
            var newSlug = await GenerateUniqueSlugAsync(candidateSlug, product.Id);
            product.Slug = newSlug;

            // Persist the retired slug so old URLs can be 301-redirected
            _context.ProductSlugHistories.Add(new ProductSlugHistory
            {
                ProductId = product.Id,
                OldSlug = oldSlug
            });

            _logger.LogInformation(
                "Product {ProductId} slug changed from '{OldSlug}' to '{NewSlug}'. History record created.",
                product.Id, oldSlug, newSlug);
        }

        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Product Update SaveChangesAsync completed successfully.");
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency exception during product update save!");
            throw;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            _logger.LogError(ex, "DbUpdateException during product update save!");
            throw;
        }

        // Invalidate caches — include old slug so stale cache entries are purged
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:{product.Id}");
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:slug:{product.Slug}");   // new slug
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:slug:{oldSlug}");         // old slug
        await _cache.RemoveByTagAsync("product-tag:list");
        await _cache.RemoveByTagAsync("product-tag:admin-list");

        _logger.LogInformation("Product update completed for ID {ProductId}.", product.Id);

        // --- Fire Merchant Center sync (non-blocking — failures are logged, never thrown) ---
        var syncProduct = await _context.Products
            .Include(p => p.Translations)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == product.Id);
        if (syncProduct != null)
            await _merchantService.SyncProductToMerchantCenterAsync(syncProduct);

        return true;
    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException($"Product with ID {id} not found.");

        // Always soft-delete so the admin can restore the product if needed.
        // Hard deletion is intentionally avoided — the frontend shows an "Archived" badge
        // and a Restore button for every soft-deleted product.
        product.IsDeleted = true;
        _logger.LogInformation(
            "Product {ProductId} soft-deleted. Product remains in database and can be restored by admin.",
            id);

        await _context.SaveChangesAsync();

        // Remove from Google Merchant Center immediately (both soft and hard delete)
        await _merchantService.DeleteProductFromMerchantCenterAsync(product.Slug);

        // Invalidate all caches for this product
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:{id}");
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:slug:{product.Slug}");
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:list");
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:admin-list");
        await _cache.RemoveByTagAsync("sitemap");
    }

    public async Task RestoreAsync(Guid id)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException($"Product with ID {id} not found.");

        product.IsDeleted = false;
        _logger.LogInformation(
            "Product {ProductId} restored.",
            id);

        await _context.SaveChangesAsync();

        // Invalidate all caches for this product
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:{id}");
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:slug:{product.Slug}");
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:list");
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:admin-list");
        await _cache.RemoveByTagAsync("sitemap");
    }

    private static ProductResponse MapToResponse(Product product, Currency currentCurrency, string languageCode)
    {
        // 1. Resolve product name/description translations
        var translation = product.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)
                          ?? product.Translations?.FirstOrDefault(t => t.LanguageCode == "en")
                          ?? product.Translations?.FirstOrDefault();

        string productName = translation?.Name ?? $"[No Name - {product.Slug}]";
        string productDesc = translation?.Description ?? "No description available";

        // 2. Map Coffee Profile
        ProductCoffeeProfileDto? coffeeProfileDto = null;
        if (product.CoffeeProfile != null)
        {
            var originName = product.CoffeeProfile.Origin?.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedName
                          ?? product.CoffeeProfile.Origin?.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                          ?? product.CoffeeProfile.Origin?.Name;

            coffeeProfileDto = new ProductCoffeeProfileDto(
                OriginId: product.CoffeeProfile.OriginId,
                OriginName: originName,
                RoastLevelNames: product.CoffeeProfile.RoastLevels?.Select(r =>
                    r.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedName
                    ?? r.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                    ?? r.Name).ToList() ?? new List<string>(),
                GrindTypeNames: product.CoffeeProfile.GrindTypes?.Select(g =>
                    g.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedName
                    ?? g.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                    ?? g.Name).ToList() ?? new List<string>(),
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

        // 3. Map Variants and Prices (sorted by weight ascending: 250g → 500g → 1kg)
        var variants = product.Variants?.DistinctBy(v => v.Id)
            .OrderBy(v => v.Options?
                .Where(o => o.ProductAttributeValue?.ProductAttribute?.Name
                    .Contains("weight", StringComparison.OrdinalIgnoreCase) == true
                    || o.ProductAttributeValue?.ProductAttribute?.Translations
                        .Any(t => t.TranslatedName.Contains("weight", StringComparison.OrdinalIgnoreCase)) == true)
                .Select(o => ParseWeightToGrams(o.ProductAttributeValue?.Value ?? ""))
                .DefaultIfEmpty(int.MaxValue)
                .Min())
            .Select(v =>
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
                StockStatus: v.StockQuantity == 0 ? "https://schema.org/OutOfStock" : "https://schema.org/InStock",
                Options: v.Options?.Select(o =>
                {
                    var attrName = o.ProductAttributeValue?.ProductAttribute?.Translations
                        ?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedName
                        ?? o.ProductAttributeValue?.ProductAttribute?.Translations
                        ?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                        ?? o.ProductAttributeValue?.ProductAttribute?.Name
                        ?? string.Empty;

                    var attrValue = o.ProductAttributeValue?.Translations
                        ?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedValue
                        ?? o.ProductAttributeValue?.Translations
                        ?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedValue
                        ?? o.ProductAttributeValue?.Value
                        ?? string.Empty;

                    return new VariantOptionDto(AttributeName: attrName, Value: attrValue);
                }).ToList() ?? new List<VariantOptionDto>(),
                Prices: mappedPrices
            );
        }).ToList() ?? new List<VariantDto>();

        var brandName = product.Brand?.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.TranslatedName
                     ?? product.Brand?.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.TranslatedName
                     ?? product.Brand?.Name;

        return new ProductResponse(
            Id: product.Id,
            Name: productName,
            Slug: product.Slug,
            Description: productDesc,
            CategoryId: product.CategoryId,
            CategoryName: product.Category?.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.Name
                          ?? product.Category?.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                          ?? "Unknown",
            BrandId: product.BrandId,
            BrandName: brandName,
            Translations: product.Translations?.Select(t => new ProductTranslationDto(
                 LanguageCode: t.LanguageCode,
                 Name: t.Name,
                 Description: t.Description,
                 MetaTitle: t.MetaTitle,
                 MetaDescription: t.MetaDescription
            )).ToList() ?? new List<ProductTranslationDto>(),
            CoffeeProfile: coffeeProfileDto,
            Specifications: product.EquipmentProfile?.Specifications,
            Variants: variants,
            CreatedAt: product.CreatedAt,
            UpdatedAt: product.UpdatedAt,
            Images: product.Images?.OrderByDescending(i => i.IsMain).Select(i => new ProductImageDto(
                Id: i.Id,
                ImageUrl: i.ImageUrl,
                IsMain: i.IsMain,
                AltText: i.AltText
            )).ToList() ?? new List<ProductImageDto>(),
            IsDeleted: product.IsDeleted
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

    /// <summary>
    /// Converts weight strings like "250g", "1kg", "2.5kg" to grams for ascending sort.
    /// Returns int.MaxValue for unrecognized values so they appear last.
    /// </summary>
    private static int ParseWeightToGrams(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return int.MaxValue;

        var v = value.Trim().ToLower();
        if (v.EndsWith("kg") && double.TryParse(v[..^2], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var kg))
            return (int)(kg * 1000);

        if (v.EndsWith("g") && double.TryParse(v[..^1], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var g))
            return (int)g;

        return int.MaxValue;
    }
}
