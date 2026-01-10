using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class CategoryService(AppDbContext context) : ICategoryService
{
    private readonly AppDbContext _context = context;

    public async Task<Guid> CreateAsync(CreateCategoryRequest request)
    {
        // Check if category with same slug already exists
        var existingCategory = await _context.Categories
            .FirstOrDefaultAsync(c => c.Slug == request.Slug);

        if (existingCategory != null)
            throw new InvalidOperationException("Category with this slug already exists.");

        var category = new Category
        {
            Name = request.Name,
            Slug = request.Slug
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return category.Id;
    }

    public async Task<CategoryResponse> GetByIdAsync(Guid id)
    {
        var category = await _context.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
            throw new KeyNotFoundException($"Category with ID {id} not found.");

        return MapToResponse(category);
    }

    public async Task<List<CategoryResponse>> GetAllAsync()
    {
        var categories = await _context.Categories
            .Include(c => c.Products)
            .ToListAsync();

        return categories.Select(MapToResponse).ToList();
    }

    public async Task UpdateAsync(Guid id, CreateCategoryRequest request)
    {
        var category = await _context.Categories
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

        category.Name = request.Name;
        category.Slug = request.Slug;
        category.UpdatedAt = DateTime.UtcNow;

        _context.Categories.Update(category);
        await _context.SaveChangesAsync();
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
    }

    private static CategoryResponse MapToResponse(Category category)
    {
        return new CategoryResponse(
            Id: category.Id,
            Name: category.Name,
            Slug: category.Slug,
            ProductCount: category.Products.Count,
            CreatedAt: category.CreatedAt,
            UpdatedAt: category.UpdatedAt
        );
    }
}
