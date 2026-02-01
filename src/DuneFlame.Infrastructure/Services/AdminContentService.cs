using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
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
                throw new NotFoundException($"About section not found: {id}");
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
                throw new NotFoundException($"About section not found: {id}");
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
                throw new NotFoundException($"About section not found: {id}");
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
