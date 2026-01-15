using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;

namespace DuneFlame.Application.Interfaces;

public interface IOriginService
{
    Task<Guid> CreateAsync(CreateOriginRequest request);
    Task<OriginResponse> GetByIdAsync(Guid id);
    Task<PagedResult<OriginResponse>> GetAllAsync(int pageNumber = 1, int pageSize = 10);
    Task UpdateAsync(Guid id, CreateOriginRequest request);
    Task DeleteAsync(Guid id);
}
