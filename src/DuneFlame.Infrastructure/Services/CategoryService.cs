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
    private const string TreeCacheKey = "categories-tree";
    private const int CacheDurationMinutes = 60;

    public async Task<Guid> CreateAsync(CreateCategoryRequest request)
    {
        // Check if category with same slug already exists
        var existingCategory = await _context.Categories
            .FirstOrDefaultAsync(c => c.Slug == request.Slug);

        if (existingCategory != null)
            throw new ConflictException("Category with this slug already exists.");

        var category = new Category
        {
            Slug = request.Slug,
            IsCoffeeCategory = request.IsCoffeeCategory,
            ParentCategoryId = request.ParentCategoryId ?? Guid.Empty
        };

        // Add default English translation
        category.Translations.Add(new CategoryTranslation
        {
            LanguageCode = "en",
            Name = request.Name
        });

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        // Invalidate flat list and tree caches
        await _cache.RemoveAsync(AllCategoriesCacheKey);
        await _cache.RemoveAsync(TreeCacheKey);
        _logger.LogInformation("Category created and caches invalidated: {CategoryId}", category.Id);

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

    public async Task<CategoryResponse> GetBySlugAsync(string slug)
    {
        var category = await _context.Categories
            .Include(c => c.Products)
            .Include(c => c.Translations)
            .FirstOrDefaultAsync(c => c.Slug == slug);

        if (category == null)
            throw new NotFoundException($"Category with slug '{slug}' not found.");

        var allById = await _context.Categories
            .Include(c => c.Translations)
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id);

        return MapToResponse(category, allById);
    }

    public async Task<List<CategoryResponse>> GetAllAsync()
    {
        try
        {
            var cachedCategories = await _cache.GetStringAsync(AllCategoriesCacheKey);
            if (cachedCategories != null)
            {
                _logger.LogInformation("Categories retrieved from cache");
                return JsonSerializer.Deserialize<List<CategoryResponse>>(cachedCategories) ?? [];
            }

            // Load all categories (including root) to build breadcrumb paths
            var allCategories = await _context.Categories
                .Include(c => c.Products)
                .Include(c => c.Translations)
                .ToListAsync();

            var allById = allCategories.ToDictionary(c => c.Id);

            // Exclude the internal root node from flat listings
            var categories = allCategories.Where(c => c.Slug != "root").ToList();

            var categoryResponses = categories.Select(c => MapToResponse(c, allById)).ToList();

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

    public async Task<List<CategoryResponse>> GetTreeAsync()
    {
        var cached = await _cache.GetStringAsync(TreeCacheKey);
        if (cached != null)
        {
            _logger.LogInformation("Category tree retrieved from cache");
            return JsonSerializer.Deserialize<List<CategoryResponse>>(cached) ?? [];
        }

        // Load all categories in one query — no recursive DB trips
        var all = await _context.Categories
            .Include(c => c.Products)
            .Include(c => c.Translations)
            .AsNoTracking()
            .ToListAsync();

        // Build a lookup: parentId → children list
        var byParent = all.GroupBy(c => c.ParentCategoryId)
                          .ToDictionary(g => g.Key, g => g.ToList());

        // Recursively maps a category and all its descendants.
        // Guard: skip any child whose Id == its own ParentCategoryId (self-referential sentinel, e.g. root).
        CategoryResponse BuildNode(Category category)
        {
            var children = byParent.TryGetValue(category.Id, out var childList)
                ? childList.Where(c => c.Id != category.Id).Select(BuildNode).ToList()
                : [];

            return MapToResponseWithChildren(category, children);
        }

        // L1 nodes: direct children of the root sentinel.
        // The root row itself has ParentCategoryId == root.Id (self-referential), so it appears
        // inside byParent[rootId]. Exclude it explicitly to prevent BuildNode from recursing into
        // itself and causing a StackOverflowException (SIGABRT / signal 6 on Cloud Run).
        var rootCategory = all.FirstOrDefault(c => c.Slug == "root");
        if (rootCategory == null) return [];

        var tree = byParent.TryGetValue(rootCategory.Id, out var l1Nodes)
            ? l1Nodes.Where(c => c.Id != rootCategory.Id).Select(BuildNode).ToList()
            : [];

        var cacheOptions = new DistributedCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(CacheDurationMinutes));

        await _cache.SetStringAsync(TreeCacheKey, JsonSerializer.Serialize(tree), cacheOptions);

        _logger.LogInformation("Category tree built: {Count} top-level nodes", tree.Count);
        return tree;
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
        category.IsCoffeeCategory = request.IsCoffeeCategory;
        if (request.ParentCategoryId.HasValue)
            category.ParentCategoryId = request.ParentCategoryId.Value;
        category.UpdatedAt = DateTime.UtcNow;

        _context.Categories.Update(category);
        await _context.SaveChangesAsync();

        // Invalidate flat list and tree caches
        await _cache.RemoveAsync(AllCategoriesCacheKey);
        await _cache.RemoveAsync(TreeCacheKey);
        _logger.LogInformation("Category updated and caches invalidated: {CategoryId}", id);
    }

    public async Task DeleteAsync(Guid id)
    {
        var category = await _context.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
            throw new NotFoundException($"Category with ID {id} not found.");

        // Prevent deleting a category that still owns sub-categories
        var hasChildren = await _context.Categories.AnyAsync(c => c.ParentCategoryId == id);
        if (hasChildren)
            throw new BadRequestException("Cannot delete a category that contains sub-categories. Remove or reassign the sub-categories first.");

        var descendantIds = await GetDescendantCategoryIdsAsync(id);
        var hasDescendantProducts = await _context.Products
            .AnyAsync(p => descendantIds.Contains(p.CategoryId));
        if (hasDescendantProducts)
            throw new BadRequestException("Cannot delete: this category or its sub-categories contain products.");

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        // Invalidate flat list and tree caches
        await _cache.RemoveAsync(AllCategoriesCacheKey);
        await _cache.RemoveAsync(TreeCacheKey);
        _logger.LogInformation("Category deleted and caches invalidated: {CategoryId}", id);
    }

    private static CategoryResponse MapToResponse(Category category, Dictionary<Guid, Category>? allById = null)
        => MapToResponseWithChildren(category, [], allById);

    private static CategoryResponse MapToResponseWithChildren(Category category, IEnumerable<CategoryResponse> children, Dictionary<Guid, Category>? allById = null)
    {
        var translation = category.Translations
            .FirstOrDefault(t => t.LanguageCode == "en")
            ?? category.Translations.FirstOrDefault();

        string? breadcrumb = allById != null ? BuildBreadcrumbPath(category, allById) : null;

        return new CategoryResponse(
            Id: category.Id,
            Name: translation?.Name ?? "Unknown",
            Slug: category.Slug,
            ProductCount: category.Products.Count,
            CreatedAt: category.CreatedAt,
            UpdatedAt: category.UpdatedAt,
            IsCoffeeCategory: category.IsCoffeeCategory,
            ParentCategoryId: category.ParentCategoryId,
            Children: children,
            BreadcrumbPath: breadcrumb
        );
    }

    private static string BuildBreadcrumbPath(Category category, Dictionary<Guid, Category> allById)
    {
        var parts = new List<string>();
        var current = category;

        while (current != null && current.Slug != "root")
        {
            var name = current.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                       ?? current.Translations.FirstOrDefault()?.Name
                       ?? "Unknown";
            parts.Insert(0, name);

            if (current.ParentCategoryId == Guid.Empty || !allById.TryGetValue(current.ParentCategoryId, out var parent))
                break;

            current = parent;
        }

        return string.Join(" > ", parts);
    }

    public async Task<bool> IsLeafCategoryAsync(Guid categoryId)
    {
        return !await _context.Categories.AnyAsync(c => c.ParentCategoryId == categoryId);
    }

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
}

