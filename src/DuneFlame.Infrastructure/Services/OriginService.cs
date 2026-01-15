using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class OriginService(AppDbContext context) : IOriginService
{
    private readonly AppDbContext _context = context;

    public async Task<Guid> CreateAsync(CreateOriginRequest request)
    {
        var origin = new Origin
        {
            Name = request.Name
        };

        _context.Origins.Add(origin);
        await _context.SaveChangesAsync();

        return origin.Id;
    }

    public async Task<OriginResponse> GetByIdAsync(Guid id)
    {
        var origin = await _context.Origins.FirstOrDefaultAsync(o => o.Id == id);

        if (origin == null)
            throw new NotFoundException($"Origin with ID {id} not found.");

        return MapToResponse(origin);
    }

    public async Task<PagedResult<OriginResponse>> GetAllAsync(int pageNumber = 1, int pageSize = 10)
    {
        // Validation
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var query = _context.Origins.AsQueryable();

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var origins = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

            var responses = origins.Select(MapToResponse).ToList();

            return new PagedResult<OriginResponse>(
                responses,
                totalCount,
                pageNumber,
                pageSize,
                totalPages
            );
        }

    public async Task UpdateAsync(Guid id, CreateOriginRequest request)
    {
        var origin = await _context.Origins.FirstOrDefaultAsync(o => o.Id == id);

        if (origin == null)
            throw new NotFoundException($"Origin with ID {id} not found.");

        origin.Name = request.Name;
        origin.UpdatedAt = DateTime.UtcNow;

        _context.Origins.Update(origin);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var origin = await _context.Origins.FirstOrDefaultAsync(o => o.Id == id);

        if (origin == null)
            throw new NotFoundException($"Origin with ID {id} not found.");

        _context.Origins.Remove(origin);
        await _context.SaveChangesAsync();
    }

    private static OriginResponse MapToResponse(Origin origin)
    {
        return new OriginResponse(
            Id: origin.Id,
            Name: origin.Name,
            CreatedAt: origin.CreatedAt,
            UpdatedAt: origin.UpdatedAt
        );
    }
}
