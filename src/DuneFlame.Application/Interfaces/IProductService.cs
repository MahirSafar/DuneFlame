using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;

namespace DuneFlame.Application.Interfaces;

public interface IProductService
{
    // Create
    Task<Guid> CreateAsync(CreateProductRequest request);

    // Read
    Task<ProductResponse> GetByIdAsync(Guid id);
    Task<PagedResult<ProductResponse>> GetAllAsync(
        int pageNumber = 1,
        int pageSize = 10,
        string? sortBy = null,
        string? search = null,
        Guid? categoryId = null);

    // Update
    Task UpdateAsync(Guid id, UpdateProductRequest request);

    // Delete
    Task DeleteAsync(Guid id);
}
