using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

public class AdminContentService(
    AppDbContext context,
    ILogger<AdminContentService> logger) : IAdminContentService
{
    private readonly AppDbContext _context = context;
    private readonly ILogger<AdminContentService> _logger = logger;

    // Slider endpoints
    public async Task<List<SliderDto>> GetAllSlidersAsync()
    {
        try
        {
            var sliders = await _context.Sliders
                .OrderBy(s => s.Order)
                .ToListAsync();

            return sliders.Select(s => new SliderDto(
                s.Id, s.Title, s.Subtitle, s.ImageUrl, s.TargetUrl, s.Order, s.IsActive
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sliders");
            throw;
        }
    }

    public async Task<SliderDto> GetSliderByIdAsync(Guid id)
    {
        try
        {
            var slider = await _context.Sliders.FindAsync(id);
            if (slider == null)
            {
                throw new KeyNotFoundException($"Slider not found: {id}");
            }

            return new SliderDto(
                slider.Id, slider.Title, slider.Subtitle, slider.ImageUrl, slider.TargetUrl, slider.Order, slider.IsActive
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving slider: {SliderId}", id);
            throw;
        }
    }

    public async Task<Guid> CreateSliderAsync(CreateSliderRequest request)
    {
        try
        {
            var slider = new Slider
            {
                Title = request.Title,
                Subtitle = request.Subtitle,
                ImageUrl = request.ImageUrl,
                TargetUrl = request.TargetUrl,
                Order = request.Order,
                IsActive = request.IsActive
            };

            _context.Sliders.Add(slider);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Slider created: {SliderId}", slider.Id);
            return slider.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating slider");
            throw;
        }
    }

    public async Task UpdateSliderAsync(Guid id, CreateSliderRequest request)
    {
        try
        {
            var slider = await _context.Sliders.FindAsync(id);
            if (slider == null)
            {
                throw new KeyNotFoundException($"Slider not found: {id}");
            }

            slider.Title = request.Title;
            slider.Subtitle = request.Subtitle;
            slider.ImageUrl = request.ImageUrl;
            slider.TargetUrl = request.TargetUrl;
            slider.Order = request.Order;
            slider.IsActive = request.IsActive;

            _context.Sliders.Update(slider);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Slider updated: {SliderId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating slider: {SliderId}", id);
            throw;
        }
    }

    public async Task DeleteSliderAsync(Guid id)
    {
        try
        {
            var slider = await _context.Sliders.FindAsync(id);
            if (slider == null)
            {
                throw new KeyNotFoundException($"Slider not found: {id}");
            }

            _context.Sliders.Remove(slider);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Slider deleted: {SliderId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting slider: {SliderId}", id);
            throw;
        }
    }

    // About Section endpoints
    public async Task<List<AboutSectionDto>> GetAllAboutSectionsAsync()
    {
        try
        {
            var sections = await _context.AboutSections.ToListAsync();

            return sections.Select(s => new AboutSectionDto(
                s.Id, s.Title, s.Content, s.ImageUrl
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving about sections");
            throw;
        }
    }

    public async Task<AboutSectionDto> GetAboutSectionByIdAsync(Guid id)
    {
        try
        {
            var section = await _context.AboutSections.FindAsync(id);
            if (section == null)
            {
                throw new KeyNotFoundException($"About section not found: {id}");
            }

            return new AboutSectionDto(section.Id, section.Title, section.Content, section.ImageUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving about section: {SectionId}", id);
            throw;
        }
    }

    public async Task<Guid> CreateAboutSectionAsync(CreateAboutSectionRequest request)
    {
        try
        {
            var section = new AboutSection
            {
                Title = request.Title,
                Content = request.Content,
                ImageUrl = request.ImageUrl
            };

            _context.AboutSections.Add(section);
            await _context.SaveChangesAsync();

            _logger.LogInformation("About section created: {SectionId}", section.Id);
            return section.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating about section");
            throw;
        }
    }

    public async Task UpdateAboutSectionAsync(Guid id, CreateAboutSectionRequest request)
    {
        try
        {
            var section = await _context.AboutSections.FindAsync(id);
            if (section == null)
            {
                throw new KeyNotFoundException($"About section not found: {id}");
            }

            section.Title = request.Title;
            section.Content = request.Content;
            section.ImageUrl = request.ImageUrl;

            _context.AboutSections.Update(section);
            await _context.SaveChangesAsync();

            _logger.LogInformation("About section updated: {SectionId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating about section: {SectionId}", id);
            throw;
        }
    }

    public async Task DeleteAboutSectionAsync(Guid id)
    {
        try
        {
            var section = await _context.AboutSections.FindAsync(id);
            if (section == null)
            {
                throw new KeyNotFoundException($"About section not found: {id}");
            }

            _context.AboutSections.Remove(section);
            await _context.SaveChangesAsync();

            _logger.LogInformation("About section deleted: {SectionId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting about section: {SectionId}", id);
            throw;
        }
    }
}
