using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Services;

public class ProductService(
    AppDbContext context,
    IFileService fileService,
    IDistributedCache cache) : IProductService
{
    private readonly AppDbContext _context = context;
    private readonly IFileService _fileService = fileService;
    private readonly IDistributedCache _cache = cache;
    private const string ProductCacheKeyPrefix = "product-";
    private const int CacheDurationMinutes = 10;

    public async Task<Guid> CreateAsync(CreateProductRequest request)
    {
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Price = request.Price,
            DiscountPercentage = request.DiscountPercentage,
            StockQuantity = request.StockQuantity,
            CategoryId = request.CategoryId,
            OriginId = request.OriginId,
            RoastLevel = request.RoastLevel,
            Weight = request.Weight,
            FlavorNotes = request.FlavorNotes,
            IsActive = true
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

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
                    IsMain = !isMainSet // First image is main
                };

                if (!isMainSet)
                    isMainSet = true;

                _context.ProductImages.Add(productImage);
            }

            await _context.SaveChangesAsync();
        }

        return product.Id;
    }

    public async Task<ProductResponse> GetByIdAsync(Guid id)
    {
        var cacheKey = $"{ProductCacheKeyPrefix}{id}";

        // Try to get from cache
        var cachedData = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cachedData))
        {
            return JsonSerializer.Deserialize<ProductResponse>(cachedData)!;
        }

        // Fetch from database if not in cache
        var product = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Origin)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            throw new NotFoundException($"Product with ID {id} not found.");

        var response = MapToResponse(product);

        // Set cache for 10 minutes
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheDurationMinutes)
        };

        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), cacheOptions);

        return response;
    }

    public async Task<PagedResult<ProductResponse>> GetAllAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string? sortBy = null,
        string? search = null,
        Guid? categoryId = null)
    {
        // Validation
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100; // Max page size

        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.Origin)
            .Include(p => p.Images)
            .Where(p => p.IsActive)
            .AsQueryable();

        // Filtering
        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p =>
                p.Name.Contains(search) ||
                p.Description.Contains(search) ||
                p.FlavorNotes.Contains(search));
        }

        // Sorting
        query = sortBy?.ToLower() switch
        {
            "price-asc" => query.OrderBy(p => p.Price),
            "price-desc" => query.OrderByDescending(p => p.Price),
            "date-asc" => query.OrderBy(p => p.CreatedAt),
            "date-desc" => query.OrderByDescending(p => p.CreatedAt),
            _ => query.OrderByDescending(p => p.CreatedAt) // Default: newest first
        };

        // Pagination
        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var products = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

            var responses = products.Select(MapToResponse).ToList();

            return new PagedResult<ProductResponse>(
                responses,
                totalCount,
                pageNumber,
                pageSize,
                totalPages
            );
        }

    public async Task UpdateAsync(Guid id, UpdateProductRequest request)
    {
        var product = await _context.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            throw new NotFoundException($"Product with ID {id} not found.");

        // Step A: Delete specified images
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

        // Step B: Set main image if provided
        if (request.SetMainImageId.HasValue)
        {
            var targetImage = product.Images.FirstOrDefault(img => img.Id == request.SetMainImageId.Value);

            if (targetImage == null)
                throw new NotFoundException($"Image with ID {request.SetMainImageId.Value} not found for this product.");

            // Set all images to non-main
            foreach (var img in product.Images)
            {
                img.IsMain = false;
            }

            // Set the target image as main
            targetImage.IsMain = true;
        }

        // Step C: Add new images if provided
        if (request.Images != null && request.Images.Count > 0)
        {
            foreach (var imageFile in request.Images)
            {
                var imageUrl = await _fileService.UploadImageAsync(imageFile, "products");

                var productImage = new ProductImage
                {
                    ImageUrl = imageUrl,
                    ProductId = product.Id,
                    IsMain = false // New images are not main by default
                };

                _context.ProductImages.Add(productImage);
            }
        }

        // Step D: Update standard fields
        product.Name = request.Name;
        product.Description = request.Description;
        product.Price = request.Price;
        product.DiscountPercentage = request.DiscountPercentage;
        product.StockQuantity = request.StockQuantity;
        product.CategoryId = request.CategoryId;
        product.OriginId = request.OriginId;
        product.RoastLevel = request.RoastLevel;
        product.Weight = request.Weight;
        product.FlavorNotes = request.FlavorNotes;
        product.UpdatedAt = DateTime.UtcNow;

        _context.Products.Update(product);
        await _context.SaveChangesAsync();

        // Invalidate cache
        await _cache.RemoveAsync($"{ProductCacheKeyPrefix}{id}");
    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await _context.Products
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null)
            throw new NotFoundException($"Product with ID {id} not found.");

        // Delete images from storage
        foreach (var image in product.Images)
        {
            _fileService.DeleteFile(image.ImageUrl);
        }

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();

        // Invalidate cache
        await _cache.RemoveAsync($"{ProductCacheKeyPrefix}{id}");
    }

    private static ProductResponse MapToResponse(Product product)
    {
        return new ProductResponse(
            Id: product.Id,
            Name: product.Name,
            Description: product.Description,
            Price: product.Price,
            DiscountPercentage: product.DiscountPercentage,
            StockQuantity: product.StockQuantity,
            IsActive: product.IsActive,
            CategoryId: product.CategoryId,
            CategoryName: product.Category?.Name ?? "Unknown",
            OriginId: product.OriginId,
            OriginName: product.Origin?.Name,
            RoastLevel: product.RoastLevel,
            Weight: product.Weight,
            FlavorNotes: product.FlavorNotes,
            CreatedAt: product.CreatedAt,
            UpdatedAt: product.UpdatedAt,
            Images: product.Images.Select(i => new ProductImageDto(
                Id: i.Id,
                ImageUrl: i.ImageUrl,
                IsMain: i.IsMain
            )).ToList()
        );
    }
}
