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
/// Example: "en-US" → "en", "ar-SA" → "ar"
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
        var baseSlug = SlugGenerator.GenerateSlug(request.Name);
        var uniqueSlug = await GenerateUniqueSlugAsync(baseSlug);

        var product = new Product
        {
            Slug = uniqueSlug,
            StockInKg = request.StockInKg,
            CategoryId = request.CategoryId,
            OriginId = request.OriginId,
            IsActive = true
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
                product.RoastLevels.Add(roastLevel);
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
                product.GrindTypes.Add(grindType);
            }
        }

        // Add FlavourNotes
        if (request.FlavourNotes != null && request.FlavourNotes.Count > 0)
        {
            foreach (var flavourNote in request.FlavourNotes)
            {
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

                // Add additional translations if provided
                if (flavourNote.Translations != null && flavourNote.Translations.Count > 0)
                {
                    foreach (var translation in flavourNote.Translations)
                    {
                        // Normalize language code to 2-char format (e.g., "en-US" → "en")
                        var normalizedLanguageCode = !string.IsNullOrWhiteSpace(translation.LanguageCode) 
                            ? translation.LanguageCode.Substring(0, Math.Min(2, translation.LanguageCode.Length)).ToLower()
                            : "en";

                        note.Translations.Add(new FlavourNoteTranslation
                        {
                            LanguageCode = normalizedLanguageCode,
                            Name = translation.Name
                        });
                    }
                }

                product.FlavourNotes.Add(note);
            }
        }

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // Validate and add prices
        if (request.Prices != null && request.Prices.Count > 0)
        {
            // Check for duplicate ProductWeightId + CurrencyCode combinations
            var duplicatePrices = request.Prices
                .GroupBy(p => new { p.ProductWeightId, CurrencyCode = p.CurrencyCode ?? "USD" })
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicatePrices.Any())
            {
                var firstDup = duplicatePrices.First();
                throw new BadRequestException(
                    $"Duplicate price found: Weight ID {firstDup.ProductWeightId} already has a price for currency {firstDup.CurrencyCode}. " +
                    "Each weight-currency combination can only be specified once per product."
                );
            }

            // Validate all ProductWeightIds exist
            var requestedWeightIds = request.Prices.Select(p => p.ProductWeightId).Distinct().ToList();
            var existingWeightIds = await _context.ProductWeights
                .Where(pw => requestedWeightIds.Contains(pw.Id))
                .Select(pw => pw.Id)
                .ToListAsync();

            var invalidWeightIds = requestedWeightIds.Except(existingWeightIds).ToList();
            if (invalidWeightIds.Any())
            {
                throw new BadRequestException(
                    $"Invalid ProductWeightIds: {string.Join(", ", invalidWeightIds)}. " +
                    "Please ensure all product weight IDs exist in the system."
                );
            }

            var productPrices = request.Prices.Select(p => new ProductPrice
            {
                ProductId = product.Id,
                ProductWeightId = p.ProductWeightId,
                Price = p.Price,
                CurrencyCode = ParseCurrencyCode(p.CurrencyCode ?? "USD")
            }).ToList();

            await _context.ProductPrices.AddRangeAsync(productPrices);
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
                    .Include(p => p.Translations)
                    .Include(p => p.Origin)
                    .Include(p => p.Images)
                    .Include(p => p.Prices)
                        .ThenInclude(pr => pr.Weight)
                    .Include(p => p.RoastLevels)
                    .Include(p => p.GrindTypes)
                    .Include(p => p.FlavourNotes)
                        .ThenInclude(fn => fn.Translations)
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
                    .Include(p => p.Translations)
                    .Include(p => p.Origin)
                    .Include(p => p.Images)
                    .Include(p => p.Prices)
                        .ThenInclude(pr => pr.Weight)
                    .Include(p => p.RoastLevels)
                    .Include(p => p.GrindTypes)
                    .Include(p => p.FlavourNotes)
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
        int pageSize = 10,
        string? sortBy = null,
        string? search = null,
        Guid? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        Guid[]? roastLevelIds = null,
        Guid[]? originIds = null)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var currentCurrency = _currencyProvider.GetCurrentCurrency();
        var languageCode = ExtractLanguageFromRequest();
        _logger.LogInformation("Shop request for currency: {Currency}, language: {Language}, minPrice: {MinPrice}, maxPrice: {MaxPrice}", currentCurrency, languageCode, minPrice, maxPrice);

        // Build cache key including new filters
        var roastLevelIdsCacheKey = roastLevelIds != null ? string.Join(",", roastLevelIds.OrderBy(x => x)) : "null";
        var originIdsCacheKey = originIds != null ? string.Join(",", originIds.OrderBy(x => x)) : "null";
        var cacheKey = $"{ProductCacheKeyPrefix}:all:{pageNumber}:{pageSize}:{sortBy}:{search}:{categoryId}:{currentCurrency}:{languageCode}:{minPrice}:{maxPrice}:{roastLevelIdsCacheKey}:{originIdsCacheKey}";
        var cacheTag = $"{ProductTagPrefix}:list";

        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            async (cancellationToken) =>
            {
                var query = _context.Products
                    .Include(p => p.Category)
                    .Include(p => p.Translations)
                    .Include(p => p.Origin)
                    .Include(p => p.Images)
                    .Include(p => p.Prices)
                        .ThenInclude(pr => pr.Weight)
                    .Include(p => p.RoastLevels)
                    .Include(p => p.GrindTypes)
                    .Include(p => p.FlavourNotes)
                        .ThenInclude(fn => fn.Translations)
                    .AsSplitQuery()
                    .Where(p => p.IsActive)
                    .AsQueryable();

                if (categoryId.HasValue)
                {
                    query = query.Where(p => p.CategoryId == categoryId.Value);
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
                        p.RoastLevels.Any(r => roastLevelIds.Contains(r.Id)));
                    _logger.LogInformation("Applied roast level filter: {RoastLevelIds}", string.Join(",", roastLevelIds));
                }

                // ========== NEW: Filter by Origins (BEFORE pagination) ==========
                if (originIds != null && originIds.Length > 0)
                {
                    query = query.Where(p =>
                        originIds.Contains(p.OriginId.Value));
                    _logger.LogInformation("Applied origin filter: {OriginIds}", string.Join(",", originIds));
                }

                // ========== NEW: Extended Sorting Support ==========
                query = sortBy?.ToLower() switch
                {
                    "stock-asc" => query.OrderBy(p => p.StockInKg),
                    "stock-desc" => query.OrderByDescending(p => p.StockInKg),
                    "date-asc" => query.OrderBy(p => p.CreatedAt),
                    "date-desc" => query.OrderByDescending(p => p.CreatedAt),
                    "name-asc" => query.OrderBy(p => p.Translations.FirstOrDefault(t => t.LanguageCode == "en").Name),
                    "name-desc" => query.OrderByDescending(p => p.Translations.FirstOrDefault(t => t.LanguageCode == "en").Name),
                    "price-asc" => query.OrderBy(p => p.Prices.FirstOrDefault(pr => pr.CurrencyCode == currentCurrency).Price),
                    "price-desc" => query.OrderByDescending(p => p.Prices.FirstOrDefault(pr => pr.CurrencyCode == currentCurrency).Price),
                    _ => query.OrderByDescending(p => p.CreatedAt)
                };

                // Filter to only include products that have prices for the current currency
                query = query.Where(p => p.Prices.Any(pr => pr.CurrencyCode == currentCurrency));

                // Apply price filter only if a specific range is requested (not the default 0-100)
                // This allows all products to load initially on the Shop page
                if ((minPrice.HasValue && minPrice.Value > 0) || (maxPrice.HasValue && maxPrice.Value < 10000))
                {
                    var min = minPrice ?? 0;
                    var max = maxPrice ?? decimal.MaxValue;
                    query = query.Where(p => p.Prices.Any(pr => 
                        pr.CurrencyCode == currentCurrency && 
                        pr.Price >= min && 
                        pr.Price <= max));

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
                                                        var cacheKey = $"{ProductCacheKeyPrefix}:admin:{pageNumber}:{pageSize}:{sortBy}:{search}:{categoryId}:{currentCurrency}:{languageCode}:{minPrice}:{maxPrice}:{roastLevelIdsCacheKey}:{originIdsCacheKey}";
                                                        var cacheTag = $"{ProductTagPrefix}:admin-list";

                                                var result = await _cache.GetOrCreateAsync(
                                                    cacheKey,
                                                    async (cancellationToken) =>
                                                    {
                                                        var query = _context.Products
                                                            .Include(p => p.Category)
                                                                .ThenInclude(c => c.Translations)
                                                            .Include(p => p.Translations)
                                                            .Include(p => p.Origin)
                                                            .Include(p => p.Images)
                                                            .Include(p => p.Prices)
                                                                .ThenInclude(pr => pr.Weight)
                                                            .Include(p => p.RoastLevels)
                                                            .Include(p => p.GrindTypes)
                                                            .Include(p => p.FlavourNotes)
                                                                .ThenInclude(fn => fn.Translations)
                                                            .AsQueryable();

                                                        if (categoryId.HasValue)
                                                        {
                                                            query = query.Where(p => p.CategoryId == categoryId.Value);
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
                                                                p.RoastLevels.Any(r => roastLevelIds.Contains(r.Id)));
                                                        }

                                                        // Filter by Origins (BEFORE pagination)
                                                        if (originIds != null && originIds.Length > 0)
                                                        {
                                                            query = query.Where(p =>
                                                                originIds.Contains(p.OriginId.Value));
                                                        }

                                                        // Extended Sorting Support
                                                        query = sortBy?.ToLower() switch
                                                        {
                                                            "stock-asc" => query.OrderBy(p => p.StockInKg),
                                                            "stock-desc" => query.OrderByDescending(p => p.StockInKg),
                                                            "date-asc" => query.OrderBy(p => p.CreatedAt),
                                                            "date-desc" => query.OrderByDescending(p => p.CreatedAt),
                                                            "name-asc" => query.OrderBy(p => p.Translations.FirstOrDefault(t => t.LanguageCode == "en").Name),
                                                            "name-desc" => query.OrderByDescending(p => p.Translations.FirstOrDefault(t => t.LanguageCode == "en").Name),
                                                            "price-asc" => query.OrderBy(p => p.Prices.FirstOrDefault(pr => pr.CurrencyCode == currentCurrency).Price),
                                                            "price-desc" => query.OrderByDescending(p => p.Prices.FirstOrDefault(pr => pr.CurrencyCode == currentCurrency).Price),
                                                            _ => query.OrderByDescending(p => p.CreatedAt)
                                                        };

                                                        // Apply price filter only if a specific range is requested (not the default 0-100)
                                                        // This allows all products to load initially on the admin page
                                                        if ((minPrice.HasValue && minPrice.Value > 0) || (maxPrice.HasValue && maxPrice.Value < 10000))
                                                        {
                                                            var min = minPrice ?? 0;
                                                            var max = maxPrice ?? decimal.MaxValue;
                                                            query = query.Where(p => p.Prices.Any(pr => 
                                                                pr.CurrencyCode == currentCurrency && 
                                                                pr.Price >= min && 
                                                                pr.Price <= max));

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

                    public async Task UpdateAsync(Guid id, UpdateProductRequest request)
                    {
                        var product = await _context.Products
                            .Include(p => p.Images)
                            .Include(p => p.Prices)
                            .Include(p => p.Translations)
                            .Include(p => p.RoastLevels)
                            .Include(p => p.GrindTypes)
                            .Include(p => p.FlavourNotes)
                                .ThenInclude(fn => fn.Translations)
                            .AsSplitQuery()
                            .FirstOrDefaultAsync(p => p.Id == id) ?? throw new NotFoundException($"Product with ID {id} not found.");

                        // Capture the old name BEFORE clearing translations
                        // Prefer English translation, fallback to first available translation
                        var oldNameTranslation = product.Translations.FirstOrDefault(t => t.LanguageCode == "en")
                            ?? product.Translations.FirstOrDefault();
                        var oldName = oldNameTranslation?.Name ?? string.Empty;

                        // Handle image deletion
                        if (request.DeletedImageIds != null && request.DeletedImageIds.Count > 0)
                        {
                            var imagesToDelete = product.Images
                                .Where(img => request.DeletedImageIds.Contains(img.Id))
                                .ToList();

                            foreach (var image in imagesToDelete)
                            {
                                _fileService.DeleteFile(image.ImageUrl);
                                product.Images.Remove(image);
                            }
                        }

                        // Set main image
                        if (request.SetMainImageId.HasValue)
                        {
                            var targetImage = product.Images.FirstOrDefault(img => img.Id == request.SetMainImageId.Value) 
                                ?? throw new NotFoundException($"Image with ID {request.SetMainImageId.Value} not found for this product.");
                            foreach (var img in product.Images)
                            {
                                img.IsMain = false;
                            }

                            targetImage.IsMain = true;
                        }

                        // Add new images
                        if (request.Images != null && request.Images.Count > 0)
                        {
                            foreach (var imageFile in request.Images)
                            {
                                var imageUrl = await _fileService.UploadImageAsync(imageFile, "products");

                                var productImage = new ProductImage
                                {
                                    ImageUrl = imageUrl,
                                    ProductId = product.Id,
                                    IsMain = false
                                };

                                _context.ProductImages.Add(productImage);
                            }
                        }

                        // Update translations using navigation property clearing
                        product.Translations.Clear();
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

                        // Update slug only if name actually changed
                        if (oldName != request.Name)
                        {
                            var baseSlug = SlugGenerator.GenerateSlug(request.Name);
                            product.Slug = await GenerateUniqueSlugAsync(baseSlug, id);
                        }

                        product.StockInKg = request.StockInKg;
                        product.CategoryId = request.CategoryId;
                        product.OriginId = request.OriginId;
                        product.UpdatedAt = DateTime.UtcNow;

                        // Update M2M relationships for RoastLevels using navigation property clearing
                        product.RoastLevels.Clear();
                        if (request.RoastLevelIds != null && request.RoastLevelIds.Count > 0)
                        {
                            var roastLevels = await _context.RoastLevels
                                .Where(r => request.RoastLevelIds.Contains(r.Id))
                                .ToListAsync();
                            foreach (var roastLevel in roastLevels)
                            {
                                product.RoastLevels.Add(roastLevel);
                            }
                        }

                        // Update M2M relationships for GrindTypes using navigation property clearing
                        product.GrindTypes.Clear();
                        if (request.GrindTypeIds != null && request.GrindTypeIds.Count > 0)
                        {
                            var grindTypes = await _context.GrindTypes
                                .Where(g => request.GrindTypeIds.Contains(g.Id))
                                .ToListAsync();
                            foreach (var grindType in grindTypes)
                            {
                                product.GrindTypes.Add(grindType);
                            }
                        }

                        // Update FlavourNotes using navigation property clearing
                        product.FlavourNotes.Clear();
                        if (request.FlavourNotes != null && request.FlavourNotes.Count > 0)
                        {
                            foreach (var flavourNote in request.FlavourNotes)
                            {
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

                                // Add additional translations if provided
                                if (flavourNote.Translations != null && flavourNote.Translations.Count > 0)
                                {
                                    foreach (var translation in flavourNote.Translations)
                                    {
                                        // Normalize language code to 2-char format (e.g., "en-US" → "en")
                                        var normalizedLanguageCode = !string.IsNullOrWhiteSpace(translation.LanguageCode) 
                                            ? translation.LanguageCode.Substring(0, Math.Min(2, translation.LanguageCode.Length)).ToLower()
                                            : "en";

                                        note.Translations.Add(new FlavourNoteTranslation
                                        {
                                            LanguageCode = normalizedLanguageCode,
                                            Name = translation.Name
                                        });
                                    }
                                }

                                product.FlavourNotes.Add(note);
                            }
                        }

                        // Update prices using navigation property clearing
                        product.Prices.Clear();
                        if (request.Prices != null && request.Prices.Count > 0)
                        {
                            // Check for duplicate ProductWeightId + CurrencyCode combinations
                            var duplicatePrices = request.Prices
                                .GroupBy(p => new { p.ProductWeightId, CurrencyCode = p.CurrencyCode ?? "USD" })
                                .Where(g => g.Count() > 1)
                                .Select(g => g.Key)
                                .ToList();

                            if (duplicatePrices.Any())
                            {
                                var firstDup = duplicatePrices.First();
                                throw new BadRequestException(
                                    $"Duplicate price found: Weight ID {firstDup.ProductWeightId} already has a price for currency {firstDup.CurrencyCode}. " +
                                    "Each weight-currency combination can only be specified once per product."
                                );
                            }

                            // Validate all ProductWeightIds exist
                            var requestedWeightIds = request.Prices.Select(p => p.ProductWeightId).Distinct().ToList();
                            var existingWeightIds = await _context.ProductWeights
                                .Where(pw => requestedWeightIds.Contains(pw.Id))
                                .Select(pw => pw.Id)
                                .ToListAsync();

                            var invalidWeightIds = requestedWeightIds.Except(existingWeightIds).ToList();
                            if (invalidWeightIds.Any())
                            {
                                throw new BadRequestException(
                                    $"Invalid ProductWeightIds: {string.Join(", ", invalidWeightIds)}. " +
                                    "Please ensure all product weight IDs exist in the system."
                                );
                            }

                            var newPrices = request.Prices.Select(p => new ProductPrice
                            {
                                ProductId = product.Id,
                                ProductWeightId = p.ProductWeightId,
                                Price = p.Price,
                                CurrencyCode = ParseCurrencyCode(p.CurrencyCode ?? "USD")
                            }).ToList();

                            foreach (var price in newPrices)
                            {
                                product.Prices.Add(price);
                            }
                        }

                        // Wrap SaveChangesAsync in try-catch for concurrency handling
                        try
                        {
                            await _context.SaveChangesAsync();
                        }
                        catch (DbUpdateConcurrencyException ex)
                        {
                            _logger.LogError(ex, "Concurrency conflict occurred while updating product {ProductId}", id);
                            throw new BadRequestException(
                                "The product was modified by another user. Please refresh and try again."
                            );
                        }

                        // Invalidate all caches for this product using tag-based invalidation
                        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:{id}");
                        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:slug:{product.Slug}");
                    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await _context.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id) ?? throw new NotFoundException($"Product with ID {id} not found.");
        
        foreach (var image in product.Images)
        {
            _fileService.DeleteFile(image.ImageUrl);
        }

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();

        // Invalidate all caches for this product
        await _cache.RemoveByTagAsync($"{ProductTagPrefix}:{id}");
    }

    private ProductResponse MapToResponse(Product product, Currency currentCurrency, string languageCode)
    {
        // 1. İstənilən dildə tərcüməni axtar
        var translation = product.Translations.FirstOrDefault(t => t.LanguageCode == languageCode);

        // 2. Tapılmasa, İngilis dilini yoxla
        if (translation == null)
        {
            translation = product.Translations.FirstOrDefault(t => t.LanguageCode == "en");
        }

        // 3. O da yoxdursa, siyahıdakı İLK tərcüməni götür (bəlkə rusca və ya başqa dildədir)
        if (translation == null)
        {
            translation = product.Translations.FirstOrDefault();
        }

        // 4. Əgər məhsulun HEÇ BİR tərcüməsi yoxdursa (Data bazada xətalı məlumat),
        // Xəta atmaq əvəzinə "Dummy" (Saxta) məlumat yarat ki, səhifə çökmesin.
        if (translation == null)
        {
            translation = new ProductTranslation
            {
                Name = $"[No Name - {product.Slug}]", // Müvəqqəti ad
                Description = "No description available",
                LanguageCode = "en"
            };

            // Log-a yaz ki, admin bilsin hansı məhsul xətalıdır
            _logger.LogWarning($"Product {product.Id} has NO translations! Returning placeholder.");
        }

        // Get price for current currency
        var activePrice = product.Prices
            .FirstOrDefault(p => p.CurrencyCode == currentCurrency);

        var activePriceDto = activePrice != null 
            ? new ProductPriceDto(
                ProductPriceId: activePrice.Id,
                WeightLabel: activePrice.Weight?.Label ?? "Unknown",
                Grams: activePrice.Weight?.Grams ?? 0,
                Price: activePrice.Price,
                CurrencyCode: activePrice.CurrencyCode.ToString()
            )
            : null;

        // Get all other price variants (exclude only the specific activePrice by ID)
        // This ensures that if USD is selected with 250g, the 1kg USD variant is still available
        // in otherAvailableCurrencies for the frontend to switch weights within the same currency
        var otherCurrencies = product.Prices
            .Where(p => activePrice == null || p.Id != activePrice.Id)
            .OrderBy(p => p.CurrencyCode)
            .ThenBy(p => p.Weight?.Grams ?? 0)
            .Select(p => new CurrencyOptionDto(
                CurrencyCode: p.CurrencyCode.ToString(),
                WeightLabel: p.Weight?.Label ?? "Unknown",
                Grams: p.Weight?.Grams ?? 0,
                Price: p.Price,
                ProductPriceId: p.Id
            ))
            .ToList();

        return new ProductResponse(
            Id: product.Id,
            Name: translation.Name,
            Slug: product.Slug,
            Description: translation.Description,
            StockInKg: product.StockInKg,
            IsActive: product.IsActive,
            CategoryId: product.CategoryId,
            CategoryName: product.Category?.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.Name 
                          ?? product.Category?.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name 
                          ?? "Unknown",
            OriginId: product.OriginId,
            OriginName: product.Origin?.Name,
            RoastLevelNames: [.. product.RoastLevels.Select(r => r.Name)],
            GrindTypeNames: [.. product.GrindTypes.Select(g => g.Name)],
            RoastLevelIds: [.. product.RoastLevels.Select(r => r.Id)],
            GrindTypeIds: [.. product.GrindTypes.Select(g => g.Id)],
            Translations: [.. product.Translations.Select(t => new ProductTranslationDto(
                LanguageCode: t.LanguageCode,
                Name: t.Name,
                Description: t.Description
            ))],
            FlavourNotes: [.. product.FlavourNotes.OrderBy(fn => fn.DisplayOrder).Select(fn => 
            {
                // Get translation for current language, fallback to English
                var translation = fn.Translations.FirstOrDefault(t => t.LanguageCode == languageCode)
                    ?? fn.Translations.FirstOrDefault(t => t.LanguageCode == "en");

                return new FlavourNoteDto(
                    Id: fn.Id,
                    Name: translation?.Name ?? fn.Name,
                    DisplayOrder: fn.DisplayOrder,
                    Translations: [.. fn.Translations.Select(t => new FlavourNoteTranslationDto(
                        FlavourNoteId: fn.Id,
                        LanguageCode: t.LanguageCode,
                        Name: t.Name
                    ))]
                );
            })],
            ActivePrice: activePriceDto,
            OtherAvailableCurrencies: otherCurrencies,
            CreatedAt: product.CreatedAt,
            UpdatedAt: product.UpdatedAt,
            Images: [.. product.Images.Select(i => new ProductImageDto(
                Id: i.Id,
                ImageUrl: i.ImageUrl,
                IsMain: i.IsMain
            ))]
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

