using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class ContactService(AppDbContext context, IEmailService emailService) : IContactService
{
    private readonly AppDbContext _context = context;
    private readonly IEmailService _emailService = emailService;

    public async Task SubmitMessageAsync(ContactMessageRequest request, string? ipAddress)
    {
        var message = new ContactMessage
        {
            Name = request.Name,
            Email = request.Email,
            Subject = request.Subject,
            Message = request.Message,
            IpAddress = ipAddress,
            IsRead = false
        };

        await _context.ContactMessages.AddAsync(message);
        await _context.SaveChangesAsync();

        // Adminə bildiriş göndər
        await _emailService.SendAdminContactAlertAsync(message);
    }

    public async Task<PagedResult<ContactMessageResponse>> GetAllAdminAsync(int pageNumber = 1, int pageSize = 10, string? search = null, bool? isRead = null)
    {
        var query = _context.ContactMessages.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.ToLower();
            query = query.Where(m => 
                m.Name.ToLower().Contains(searchTerm) ||
                m.Email.ToLower().Contains(searchTerm) ||
                m.Subject.ToLower().Contains(searchTerm) ||
                m.Message.ToLower().Contains(searchTerm));
        }

        if (isRead.HasValue)
        {
            query = query.Where(m => m.IsRead == isRead.Value);
        }

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new ContactMessageResponse(
                m.Id,
                m.Name,
                m.Email,
                m.Subject,
                m.Message,
                m.IpAddress,
                m.IsRead,
                m.AdminReply,
                m.CreatedAt))
            .ToListAsync();

        return new PagedResult<ContactMessageResponse>(items, totalItems, pageNumber, pageSize, totalPages);
    }

    public async Task MarkAsReadAsync(Guid id)
    {
        var message = await _context.ContactMessages.FindAsync(id);
        if (message is null)
        {
            throw new NotFoundException($"Contact message with ID '{id}' not found.");
        }

        message.IsRead = true;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var message = await _context.ContactMessages.FindAsync(id);
        if (message is null)
        {
            throw new NotFoundException($"Contact message with ID '{id}' not found.");
        }

        _context.ContactMessages.Remove(message);
        await _context.SaveChangesAsync();
    }
}