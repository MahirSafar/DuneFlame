using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Services;

public class CategoryService(
    AppDbContext context,
    IDistributedCache cache,
    ILogger<CategoryService> logger) : ICategoryService
{
    private readonly AppDbContext _context = context;
    private readonly IDistributedCache _cache = cache;
    private readonly ILogger<CategoryService> _logger = logger;
    private const string CacheKeyPrefix = "categories-";
    private const string AllCategoriesCacheKey = "categories-all";
    private const int CacheDurationMinutes = 60; // 1 hour

    public async Task<Guid> CreateAsync(CreateCategoryRequest request)
    {
        // Check if category with same slug already exists
        var existingCategory = await _context.Categories
            .FirstOrDefaultAsync(c => c.Slug == request.Slug);

        if (existingCategory != null)
            throw new ConflictException("Category with this slug already exists.");

        var category = new Category
        {
            Slug = request.Slug
        };

        // Add default English translation
        category.Translations.Add(new CategoryTranslation
        {
            LanguageCode = "en",
            Name = request.Name
        });

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        // Invalidate cache
        await _cache.RemoveAsync(AllCategoriesCacheKey);
        _logger.LogInformation("Category created and cache invalidated: {CategoryId}", category.Id);

        return category.Id;
    }

    public async Task<CategoryResponse> GetByIdAsync(Guid id)
    {
        var category = await _context.Categories
            .Include(c => c.Products)
            .Include(c => c.Translations)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
            throw new NotFoundException($"Category with ID {id} not found.");

        return MapToResponse(category);
    }

    public async Task<List<CategoryResponse>> GetAllAsync()
    {
        try
        {
            // Try to get from cache first
            var cachedCategories = await _cache.GetStringAsync(AllCategoriesCacheKey);
            if (cachedCategories != null)
            {
                _logger.LogInformation("Categories retrieved from cache");
                return JsonSerializer.Deserialize<List<CategoryResponse>>(cachedCategories) ?? [];
            }

            // Get from database
            var categories = await _context.Categories
                .Include(c => c.Products)
                .Include(c => c.Translations)
                .ToListAsync();

            var categoryResponses = categories.Select(MapToResponse).ToList();

            // Cache the result
            var cacheOptions = new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes));

            var serializedCategories = JsonSerializer.Serialize(categoryResponses);
            await _cache.SetStringAsync(AllCategoriesCacheKey, serializedCategories, cacheOptions);

            _logger.LogInformation("Categories retrieved from database and cached");
            return categoryResponses;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving categories");
            throw;
        }
    }

    public async Task UpdateAsync(Guid id, CreateCategoryRequest request)
    {
        var category = await _context.Categories
            .Include(c => c.Translations)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
            throw new KeyNotFoundException($"Category with ID {id} not found.");

        // Check if new slug is already taken by another category
        if (category.Slug != request.Slug)
        {
            var existingCategory = await _context.Categories
                .FirstOrDefaultAsync(c => c.Slug == request.Slug);

            if (existingCategory != null)
                throw new InvalidOperationException("Category with this slug already exists.");
        }

        // Update translation for English
        var enTranslation = category.Translations.FirstOrDefault(t => t.LanguageCode == "en");
        if (enTranslation != null)
        {
            enTranslation.Name = request.Name;
        }

        category.Slug = request.Slug;
        category.UpdatedAt = DateTime.UtcNow;

        _context.Categories.Update(category);
        await _context.SaveChangesAsync();

        // Invalidate cache
        await _cache.RemoveAsync(AllCategoriesCacheKey);
        _logger.LogInformation("Category updated and cache invalidated: {CategoryId}", id);
    }

    public async Task DeleteAsync(Guid id)
    {
        var category = await _context.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
            throw new KeyNotFoundException($"Category with ID {id} not found.");

        if (category.Products.Any())
            throw new InvalidOperationException("Cannot delete category with products. Please delete products first.");

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        // Invalidate cache
        await _cache.RemoveAsync(AllCategoriesCacheKey);
        _logger.LogInformation("Category deleted and cache invalidated: {CategoryId}", id);
    }

    private static CategoryResponse MapToResponse(Category category)
    {
        // Get English translation with fallback
        var translation = category.Translations
            .FirstOrDefault(t => t.LanguageCode == "en")
            ?? category.Translations.FirstOrDefault();

        var name = translation?.Name ?? "Unknown";

        return new CategoryResponse(
            Id: category.Id,
            Name: name,
            Slug: category.Slug,
            ProductCount: category.Products.Count,
            CreatedAt: category.CreatedAt,
            UpdatedAt: category.UpdatedAt
        );
    }
}

