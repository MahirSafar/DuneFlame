namespace DuneFlame.Application.Interfaces;

public interface ICategoryService
{
    // Create
    Task<Guid> CreateAsync(CreateCategoryRequest request);

    // Read
    Task<CategoryResponse> GetByIdAsync(Guid id);
    Task<CategoryResponse> GetBySlugAsync(string slug);
    Task<List<CategoryResponse>> GetAllAsync();
    Task<List<CategoryResponse>> GetTreeAsync();

    // Update
    Task UpdateAsync(Guid id, CreateCategoryRequest request);

    // Delete
    Task DeleteAsync(Guid id);

    // Helpers
    Task<bool> IsLeafCategoryAsync(Guid categoryId);
}

public record CreateCategoryRequest(
    string Name,
    string Slug,
    bool IsCoffeeCategory,
    Guid? ParentCategoryId = null
);

public record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    int ProductCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    bool IsCoffeeCategory,
    Guid ParentCategoryId,
    IEnumerable<CategoryResponse> Children,
    string? BreadcrumbPath = null
);
