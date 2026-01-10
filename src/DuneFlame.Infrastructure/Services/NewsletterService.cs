using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Services;

public class NewsletterService(AppDbContext context, IEmailService emailService, ILogger<NewsletterService> logger) : INewsletterService
{
    private readonly AppDbContext _context = context;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger<NewsletterService> _logger = logger;
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
    public async Task SendToAllAsync(BulkEmailRequest request)
    {
        // Yalnız təsdiqlənmiş abunəçiləri gətir
        var subscribers = await _context.NewsletterSubscriptions
                    .Where(s => s.IsVerified)
                    .ToListAsync();

        int successCount = 0;
        int failureCount = 0;

        // QEYD: Real layihədə bu hissə Background Job (Hangfire/RabbitMQ) ilə edilməlidir.
        // 1000+ istifadəçi varsa, bu dövr API-ni dondura bilər.
        // Hələlik sadə "foreach" ilə edirik.

        foreach (var sub in subscribers)
        {
            var unsubscribeLink = $"https://localhost:7190/api/v1/newsletter/unsubscribe?token={sub.UnsubscribeToken}";
            var footer = $"<br/><hr/><small>Don't want these emails? <a href='{unsubscribeLink}'>Unsubscribe</a></small>";

            try
            {
                // Artıq birbaşa Generic metodu çağırırıq
                await _emailService.SendGenericEmailAsync(sub.Email, request.Subject, request.Content + footer);
                successCount++;
            }
            catch (Exception ex)
            {
                // CATCH BLOKU DOLDURULDU: Xətanı loglayırıq ki, hansı mailə getmədiyini bilək
                failureCount++;
                _logger.LogError(ex, "Failed to send newsletter to {Email}", sub.Email);
            }
        }

        _logger.LogInformation("Newsletter sending completed. Success: {Success}, Failed: {Failed}", successCount, failureCount);
    }
}