using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;

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
}