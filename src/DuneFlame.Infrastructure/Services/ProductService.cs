using DuneFlame.Application.Common;
using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Product service with HybridCache support and currency-aware pricing.
/// Currency is determined by ICurrencyProvider (from X-Currency header).
/// </summary>
public class ProductService(
    AppDbContext context,
    IFileService fileService,
    HybridCache cache,
    ICurrencyProvider currencyProvider,
    ILogger<ProductService> logger) : IProductService
{
    private readonly AppDbContext _context = context;
    private readonly IFileService _fileService = fileService;
    private readonly HybridCache _cache = cache;
    private readonly ICurrencyProvider _currencyProvider = currencyProvider;
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
            Name = request.Name,
            Slug = uniqueSlug,
            Description = request.Description,
            StockInKg = request.StockInKg,
            CategoryId = request.CategoryId,
            OriginId = request.OriginId,
            IsActive = true
        };

        // Add M2M relationships for RoastLevels
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

        // Add M2M relationships for GrindTypes
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

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        // Add prices
        if (request.Prices != null && request.Prices.Count > 0)
        {
            var productPrices = request.Prices.Select(p => new ProductPrice
            {
                ProductId = product.Id,
                ProductWeightId = p.ProductWeightId,
                Price = p.Price
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
        var cacheKey = $"{ProductCacheKeyPrefix}:id:{id}:{currentCurrency}";

        var cacheTag = $"{ProductTagPrefix}:{id}";

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async (cancellationToken) =>
            {
                var product = await _context.Products
                    .Include(p => p.Category)
                    .Include(p => p.Origin)
                    .Include(p => p.Images)
                    .Include(p => p.Prices)
                        .ThenInclude(pr => pr.Weight)
                    .Include(p => p.RoastLevels)
                    .Include(p => p.GrindTypes)
                    .FirstOrDefaultAsync(p => p.Id == id, cancellationToken) 
                    ?? throw new NotFoundException($"Product with ID {id} not found.");

                return MapToResponse(product, currentCurrency);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(CacheDurationSeconds)
            },
            tags: new[] { cacheTag } // <--- BU SƏTİR MÜTLƏQDİR!
        );

        return response;
    }

    public async Task<ProductResponse> GetBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug cannot be empty.", nameof(slug));

        var currentCurrency = _currencyProvider.GetCurrentCurrency();
        var cacheKey = $"{ProductCacheKeyPrefix}:slug:{slug}:{currentCurrency}";

        var cacheTag = $"{ProductTagPrefix}:slug:{slug}";

        var response = await _cache.GetOrCreateAsync(
            cacheKey,
            async (cancellationToken) =>
            {
                var product = await _context.Products
                    .Include(p => p.Category)
                    .Include(p => p.Origin)
                    .Include(p => p.Images)
                    .Include(p => p.Prices)
                        .ThenInclude(pr => pr.Weight)
                    .Include(p => p.RoastLevels)
                    .Include(p => p.GrindTypes)
                    .AsSplitQuery()
                    .Where(p => p.IsActive && p.Slug == slug)
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException($"Product with slug '{slug}' not found.");

                return MapToResponse(product, currentCurrency);
            },
            new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(CacheDurationSeconds)
            },
            tags: new[] { cacheTag } // <--- BU SƏTİR MÜTLƏQDİR!
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
        decimal? maxPrice = null)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var currentCurrency = _currencyProvider.GetCurrentCurrency();
        _logger.LogInformation("Shop request for currency: {Currency}, minPrice: {MinPrice}, maxPrice: {MaxPrice}", currentCurrency, minPrice, maxPrice);

        var cacheKey = $"{ProductCacheKeyPrefix}:all:{pageNumber}:{pageSize}:{sortBy}:{search}:{categoryId}:{currentCurrency}:{minPrice}:{maxPrice}";
        var cacheTag = $"{ProductTagPrefix}:list";

        var result = await _cache.GetOrCreateAsync(
            cacheKey,
            async (cancellationToken) =>
            {
                var query = _context.Products
                    .Include(p => p.Category)
                    .Include(p => p.Origin)
                    .Include(p => p.Images)
                    .Include(p => p.Prices)
                        .ThenInclude(pr => pr.Weight)
                    .Include(p => p.RoastLevels)
                    .Include(p => p.GrindTypes)
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
                        p.Name.Contains(search) ||
                        p.Description.Contains(search));
                }

                // Sorting
                query = sortBy?.ToLower() switch
                {
                    "stock-asc" => query.OrderBy(p => p.StockInKg),
                    "stock-desc" => query.OrderByDescending(p => p.StockInKg),
                    "date-asc" => query.OrderBy(p => p.CreatedAt),
                    "date-desc" => query.OrderByDescending(p => p.CreatedAt),
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

                var responses = products.Select(p => MapToResponse(p, currentCurrency)).ToList();

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
                                decimal? maxPrice = null)
                            {
                                if (pageNumber < 1) pageNumber = 1;
                                if (pageSize < 1) pageSize = 10;
                                if (pageSize > 100) pageSize = 100;

                                var currentCurrency = _currencyProvider.GetCurrentCurrency();
                                var cacheKey = $"{ProductCacheKeyPrefix}:admin:{pageNumber}:{pageSize}:{sortBy}:{search}:{categoryId}:{currentCurrency}:{minPrice}:{maxPrice}";
                                var cacheTag = $"{ProductTagPrefix}:admin-list";

                        var result = await _cache.GetOrCreateAsync(
                            cacheKey,
                            async (cancellationToken) =>
                            {
                                var query = _context.Products
                                    .Include(p => p.Category)
                                    .Include(p => p.Origin)
                                    .Include(p => p.Images)
                                    .Include(p => p.Prices)
                                        .ThenInclude(pr => pr.Weight)
                                    .Include(p => p.RoastLevels)
                                    .Include(p => p.GrindTypes)
                                    .AsQueryable();

                                if (categoryId.HasValue)
                                {
                                    query = query.Where(p => p.CategoryId == categoryId.Value);
                                }

                                if (!string.IsNullOrWhiteSpace(search))
                                {
                                    query = query.Where(p =>
                                        p.Name.Contains(search) ||
                                        p.Description.Contains(search));
                                }

                                // Sorting
                                query = sortBy?.ToLower() switch
                                {
                                    "stock-asc" => query.OrderBy(p => p.StockInKg),
                                    "stock-desc" => query.OrderByDescending(p => p.StockInKg),
                                    "date-asc" => query.OrderBy(p => p.CreatedAt),
                                    "date-desc" => query.OrderByDescending(p => p.CreatedAt),
                                    _ => query.OrderByDescending(p => p.CreatedAt)
                                };

                                // Filter to only include products that have prices for the current currency
                                query = query.Where(p => p.Prices.Any(pr => pr.CurrencyCode == currentCurrency));

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

                                var responses = products.Select(p => MapToResponse(p, currentCurrency)).ToList();

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
            .Include(p => p.RoastLevels)
            .Include(p => p.GrindTypes)
            .FirstOrDefaultAsync(p => p.Id == id) ?? throw new NotFoundException($"Product with ID {id} not found.");

        // Handle image deletion
        if (request.DeletedImageIds != null && request.DeletedImageIds.Count > 0)
        {
            var imagesToDelete = product.Images
                .Where(img => request.DeletedImageIds.Contains(img.Id))
                .ToList();

            foreach (var image in imagesToDelete)
            {
                _fileService.DeleteFile(image.ImageUrl);
                _context.ProductImages.Remove(image);
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

        // Update basic fields
        product.Name = request.Name;

        if (product.Name != request.Name)
        {
            var baseSlug = SlugGenerator.GenerateSlug(request.Name);
            product.Slug = await GenerateUniqueSlugAsync(baseSlug, id);
        }

        product.Description = request.Description;
        product.StockInKg = request.StockInKg;
        product.CategoryId = request.CategoryId;
        product.OriginId = request.OriginId;
        product.UpdatedAt = DateTime.UtcNow;

        // Update M2M relationships for RoastLevels
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

        // Update M2M relationships for GrindTypes
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

        // Update prices
        _context.ProductPrices.RemoveRange(product.Prices);
        if (request.Prices != null && request.Prices.Count > 0)
        {
            var newPrices = request.Prices.Select(p => new ProductPrice
            {
                ProductId = product.Id,
                ProductWeightId = p.ProductWeightId,
                Price = p.Price
            }).ToList();

            await _context.ProductPrices.AddRangeAsync(newPrices);
        }

        _context.Products.Update(product);
        await _context.SaveChangesAsync();

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

    private static ProductResponse MapToResponse(Product product, Currency currentCurrency)
    {
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
            Name: product.Name,
            Slug: product.Slug,
            Description: product.Description,
            StockInKg: product.StockInKg,
            IsActive: product.IsActive,
            CategoryId: product.CategoryId,
            CategoryName: product.Category?.Name ?? "Unknown",
            OriginId: product.OriginId,
            OriginName: product.Origin?.Name,
            RoastLevelNames: [.. product.RoastLevels.Select(r => r.Name)],
            GrindTypeNames: [.. product.GrindTypes.Select(g => g.Name)],
            RoastLevelIds: [.. product.RoastLevels.Select(r => r.Id)],
            GrindTypeIds: [.. product.GrindTypes.Select(g => g.Id)],
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
}
