using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.User;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Configuration;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DuneFlame.Infrastructure.Services;

public class NewsletterService(
    AppDbContext context, 
    IEmailService emailService, 
    IOptions<ClientUrls> clientUrls,
    ILogger<NewsletterService> logger) : INewsletterService
{
    private readonly AppDbContext _context = context;
    private readonly IEmailService _emailService = emailService;
    private readonly ClientUrls _clientUrls = clientUrls.Value;
    private readonly ILogger<NewsletterService> _logger = logger;
    public async Task SubscribeAsync(NewsletterRequest request)
    {
        var existing = await _context.NewsletterSubscriptions
            .FirstOrDefaultAsync(n => n.Email == request.Email);

        if (existing != null)
        {
            // Already subscribed - do nothing
            _logger.LogInformation("Newsletter subscription attempt for already subscribed email: {Email}", request.Email);
            return;
        }

        // Auto-verify: Single opt-in approach
        var sub = new NewsletterSubscription
        {
            Email = request.Email,
            IsVerified = true,
            UnsubscribeToken = Guid.NewGuid().ToString("N"),
            Source = "API"
        };

        await _context.NewsletterSubscriptions.AddAsync(sub);
        await _context.SaveChangesAsync();

        _logger.LogInformation("New newsletter subscription created and auto-verified: {Email}", request.Email);
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
            var unsubscribeLink = $"{_clientUrls.ApiBaseUrl}/api/v1/newsletter/unsubscribe?token={sub.UnsubscribeToken}";
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

    public async Task<PagedResult<NewsletterSubscription>> GetAllSubscribersAsync(int pageNumber, int pageSize, string? search)
    {
        var query = _context.NewsletterSubscriptions.AsQueryable();

        // Apply search filter if provided
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Email.Contains(search));
        }

        // Get total count
        var totalCount = await query.CountAsync();

        // Calculate total pages
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        // Get paginated items, sorted by CreatedAt descending
        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<NewsletterSubscription>(items, totalCount, pageNumber, pageSize, totalPages);
    }
}