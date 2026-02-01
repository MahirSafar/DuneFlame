using DuneFlame.Application.DTOs.Admin.Slider;
using DuneFlame.Application.DTOs.Common;

namespace DuneFlame.Application.Interfaces;

public interface ISliderService
{
    Task<Guid> CreateAsync(CreateSliderRequest request);
    Task<SliderResponse> GetByIdAsync(Guid id);
    Task<PagedResult<SliderResponse>> GetAllAsync(int pageNumber = 1, int pageSize = 10);
    Task UpdateAsync(Guid id, UpdateSliderRequest request);
    Task DeleteAsync(Guid id);
}
