using DuneFlame.Application.DTOs.Admin;

namespace DuneFlame.Application.Interfaces;

public interface IAdminContentService
{
    // Slider endpoints
    Task<List<SliderDto>> GetAllSlidersAsync();
    Task<SliderDto> GetSliderByIdAsync(Guid id);
    Task<Guid> CreateSliderAsync(CreateSliderRequest request);
    Task UpdateSliderAsync(Guid id, CreateSliderRequest request);
    Task DeleteSliderAsync(Guid id);

    // About Section endpoints
    Task<List<AboutSectionDto>> GetAllAboutSectionsAsync();
    Task<AboutSectionDto> GetAboutSectionByIdAsync(Guid id);
    Task<Guid> CreateAboutSectionAsync(CreateAboutSectionRequest request);
    Task UpdateAboutSectionAsync(Guid id, CreateAboutSectionRequest request);
    Task DeleteAboutSectionAsync(Guid id);
}
