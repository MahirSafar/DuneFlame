using DuneFlame.Application.DTOs.Admin.Slider;
using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

public class SliderService(
    AppDbContext context,
    IFileService fileService,
    ILogger<SliderService> logger) : ISliderService
{
    private readonly AppDbContext _context = context;
    private readonly IFileService _fileService = fileService;
    private readonly ILogger<SliderService> _logger = logger;

    public async Task<Guid> CreateAsync(CreateSliderRequest request)
    {
        try
        {
            if (request.Image == null || request.Image.Length == 0)
                throw new ArgumentException("Image is required.");

            if (request.Translations == null || request.Translations.Count == 0)
                throw new ArgumentException("At least one translation is required.");

            // Upload image
            string imageUrl = await _fileService.UploadImageAsync(request.Image, "sliders");

            // Create slider entity
            var slider = new Slider
            {
                ImageUrl = imageUrl,
                Order = request.Order,
                IsActive = true
            };

            // Add translations
            foreach (var translation in request.Translations)
            {
                slider.Translations.Add(new SliderTranslation
                {
                    LanguageCode = translation.LanguageCode.Substring(0, 2),
                    Title = translation.Title,
                    Subtitle = translation.Subtitle,
                    ButtonText = translation.ButtonText
                });
            }

            _context.Sliders.Add(slider);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Slider created successfully: {SliderId}", slider.Id);
            return slider.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating slider");
            throw;
        }
    }

    public async Task<SliderResponse> GetByIdAsync(Guid id)
    {
        try
        {
            var slider = await _context.Sliders
                .Include(s => s.Translations)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (slider == null)
                throw new NotFoundException($"Slider with ID {id} not found.");

            return MapToResponse(slider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving slider: {SliderId}", id);
            throw;
        }
    }

    public async Task<PagedResult<SliderResponse>> GetAllAsync(int pageNumber = 1, int pageSize = 10)
    {
        try
        {
            // Validation
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var query = _context.Sliders.AsQueryable();

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            var sliders = await query
                .Include(s => s.Translations)
                .OrderBy(s => s.Order)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var responses = sliders.Select(MapToResponse).ToList();

            return new PagedResult<SliderResponse>(
                responses,
                totalCount,
                pageNumber,
                pageSize,
                totalPages
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sliders");
            throw;
        }
    }

    public async Task UpdateAsync(Guid id, UpdateSliderRequest request)
    {
        try
        {
            var slider = await _context.Sliders
                .Include(s => s.Translations)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (slider == null)
                throw new NotFoundException($"Slider with ID {id} not found.");

            // Update image if provided
            if (request.Image != null && request.Image.Length > 0)
            {
                // Delete old image
                _fileService.DeleteFile(slider.ImageUrl);

                // Upload new image
                slider.ImageUrl = await _fileService.UploadImageAsync(request.Image, "sliders");
            }

            // Update properties
            slider.Order = request.Order;
            slider.IsActive = request.IsActive;
            slider.UpdatedAt = DateTime.UtcNow;

            // Update translations - stable approach
            if (request.Translations != null && request.Translations.Count > 0)
            {
                // Remove old translations from context
                _context.SliderTranslations.RemoveRange(slider.Translations);
                slider.Translations.Clear();

                // Add new translations
                foreach (var translation in request.Translations)
                {
                    slider.Translations.Add(new SliderTranslation
                    {
                        LanguageCode = translation.LanguageCode.Substring(0, 2),
                        Title = translation.Title,
                        Subtitle = translation.Subtitle,
                        ButtonText = translation.ButtonText,
                        SliderId = id
                    });
                }
            }

            _context.Sliders.Update(slider);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Slider updated successfully: {SliderId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating slider: {SliderId}", id);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            var slider = await _context.Sliders
                .Include(s => s.Translations)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (slider == null)
                throw new NotFoundException($"Slider with ID {id} not found.");

            // Delete image file
            _fileService.DeleteFile(slider.ImageUrl);

            // Remove slider (translations will be removed by cascade delete)
            _context.Sliders.Remove(slider);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Slider deleted successfully: {SliderId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting slider: {SliderId}", id);
            throw;
        }
    }

    private static SliderResponse MapToResponse(Slider slider)
    {
        return new SliderResponse(
            Id: slider.Id,
            ImageUrl: slider.ImageUrl,
            Order: slider.Order,
            IsActive: slider.IsActive,
            Translations: slider.Translations
                .Select(t => new SliderTranslationResponseDto(
                    Id: t.Id,
                    LanguageCode: t.LanguageCode,
                    Title: t.Title,
                    Subtitle: t.Subtitle,
                    ButtonText: t.ButtonText
                ))
                .ToList(),
            CreatedAt: slider.CreatedAt,
            UpdatedAt: slider.UpdatedAt
        );
    }
}
