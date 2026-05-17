using DuneFlame.Application.DTOs.Admin;
using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
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
            Phone = request.Phone,
            Subject = request.Subject,
            InquiryType = request.InquiryType,
            Message = request.Message,
            IpAddress = ipAddress,
            IsRead = false
        };

        await _context.ContactMessages.AddAsync(message);
        await _context.SaveChangesAsync();

        // Adminə bildiriş göndər
        await _emailService.SendAdminContactAlertAsync(message);
    }

    public async Task<PagedResult<ContactMessageResponse>> GetAllAdminAsync(AdminContactsQuery query)
    {
        var query2 = _context.ContactMessages.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchTerm = query.Search.ToLower();
            query2 = query2.Where(m =>
                m.Name.ToLower().Contains(searchTerm) ||
                m.Email.ToLower().Contains(searchTerm) ||
                (m.Subject != null && m.Subject.ToLower().Contains(searchTerm)) ||
                m.Message.ToLower().Contains(searchTerm));
        }

        if (query.IsRead.HasValue)
        {
            query2 = query2.Where(m => m.IsRead == query.IsRead.Value);
        }

        if (query.InquiryType.HasValue)
        {
            query2 = query2.Where(m => m.InquiryType == query.InquiryType.Value);
        }

        var totalItems = await query2.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)query.PageSize);

        var items = await query2
            .OrderByDescending(m => m.CreatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(m => new ContactMessageResponse(
                m.Id,
                m.Name,
                m.Email,
                m.Phone,
                m.Subject,
                m.InquiryType,
                m.Message,
                m.IpAddress,
                m.IsRead,
                m.AdminReply,
                m.CreatedAt))
            .ToListAsync();

        return new PagedResult<ContactMessageResponse>(items, totalItems, query.PageNumber, query.PageSize, totalPages);
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