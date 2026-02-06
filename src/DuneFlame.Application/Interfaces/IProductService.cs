using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;

namespace DuneFlame.Application.Interfaces;

public interface IProductService
{
    // Create
    Task<Guid> CreateAsync(CreateProductRequest request);

    // Read - Currency is now determined by ICurrencyProvider (scoped to request)
    Task<ProductResponse> GetByIdAsync(Guid id);
    Task<ProductResponse> GetBySlugAsync(string slug);
    Task<PagedResult<ProductResponse>> GetAllAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string? sortBy = null,
        string? search = null,
        Guid? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        Guid[]? roastLevelIds = null,
        Guid[]? originIds = null);
    Task<PagedResult<ProductResponse>> GetAllAdminAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string? sortBy = null,
        string? search = null,
        Guid? categoryId = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        Guid[]? roastLevelIds = null,
        Guid[]? originIds = null);

    // Update
    Task UpdateAsync(Guid id, UpdateProductRequest request);

    // Delete
    Task DeleteAsync(Guid id);
}
