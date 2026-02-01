using DuneFlame.Application.DTOs.Admin;

namespace DuneFlame.Application.Interfaces;

public interface IAdminContentService
{
    // About Section endpoints
    Task<List<AboutSectionDto>> GetAllAboutSectionsAsync();
    Task<AboutSectionDto> GetAboutSectionByIdAsync(Guid id);
    Task<Guid> CreateAboutSectionAsync(CreateAboutSectionRequest request);
    Task UpdateAboutSectionAsync(Guid id, CreateAboutSectionRequest request);
    Task DeleteAboutSectionAsync(Guid id);
}
