using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class NewsletterService(AppDbContext context, IEmailService emailService) : INewsletterService
{
    private readonly AppDbContext _context = context;
    private readonly IEmailService _emailService = emailService;

    public async Task SubscribeAsync(NewsletterRequest request)
    {
        var existing = await _context.NewsletterSubscriptions
            .FirstOrDefaultAsync(n => n.Email == request.Email);

        if (existing != null)
        {
            if (existing.IsVerified) return; // Artıq abunədir
            // Təsdiqləməyibsə, maili təkrar göndər
        }

        var token = Guid.NewGuid().ToString("N");

        if (existing == null)
        {
            var sub = new NewsletterSubscription
            {
                Email = request.Email,
                VerificationToken = token,
                IsVerified = false,
                Source = "API"
            };
            await _context.NewsletterSubscriptions.AddAsync(sub);
        }
        else
        {
            existing.VerificationToken = token; // Tokeni yenilə
        }

        await _context.SaveChangesAsync();

        // Mail göndər (Bunu IEmailService-ə əlavə etməliyik, aşağıda edəcəyik)
        await _emailService.SendNewsletterVerificationAsync(request.Email, token);
    }

    public async Task<bool> VerifyEmailAsync(string token)
    {
        var sub = await _context.NewsletterSubscriptions
            .FirstOrDefaultAsync(n => n.VerificationToken == token);

        if (sub == null) return false;

        sub.IsVerified = true;
        sub.VerificationToken = null; // Tokeni silirik
        sub.UnsubscribeToken = Guid.NewGuid().ToString("N"); // Çıxış tokeni veririk

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task UnsubscribeAsync(string token)
    {
        var sub = await _context.NewsletterSubscriptions
            .FirstOrDefaultAsync(n => n.UnsubscribeToken == token);

        if (sub != null)
        {
            _context.NewsletterSubscriptions.Remove(sub);
            await _context.SaveChangesAsync();
        }
    }
}