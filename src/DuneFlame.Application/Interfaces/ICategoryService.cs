namespace DuneFlame.Application.Interfaces;

public interface ICategoryService
{
    // Create
    Task<Guid> CreateAsync(CreateCategoryRequest request);

    // Read
    Task<CategoryResponse> GetByIdAsync(Guid id);
    Task<List<CategoryResponse>> GetAllAsync();

    // Update
    Task UpdateAsync(Guid id, CreateCategoryRequest request);

    // Delete
    Task DeleteAsync(Guid id);
}

public record CreateCategoryRequest(
    string Name,
    string Slug
);

public record CategoryResponse(
    Guid Id,
    string Name,
    string Slug,
    int ProductCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
