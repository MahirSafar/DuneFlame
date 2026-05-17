using DuneFlame.Application.DTOs.Wholesale;
using Microsoft.AspNetCore.Http;

namespace DuneFlame.Infrastructure.Services;

public class WholesaleService(
    AppDbContext context,
    IEmailService emailService,
    ILogger<WholesaleService> logger,
    IHttpContextAccessor httpContextAccessor) : IWholesaleService
{
    private readonly AppDbContext _context = context;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger<WholesaleService> _logger = logger;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public async Task SubmitLeadAsync(CreateWholesaleLeadRequest request)
    {
        var lead = new WholesaleLead
        {
            FullName     = request.FullName,
            BusinessName = request.BusinessName,
            Email        = request.Email,
            Phone        = request.Phone,
            BusinessType = request.BusinessType,
            MonthlyVolume = request.MonthlyVolume,
            Message      = request.Message,
            IsReviewed   = false
        };

        await _context.WholesaleLeads.AddAsync(lead);
        await _context.SaveChangesAsync();

        _logger.LogInformation("WholesaleService: Lead saved for {Email}", lead.Email);

        // Fire both emails concurrently — neither failing should block the other
        var lang = _httpContextAccessor.HttpContext?.Request.Headers["Accept-Language"]
                       .ToString().Split(',', ';')[0].Trim().ToLowerInvariant() ?? "en";
        var alertTask        = _emailService.SendWholesaleLeadAlertAsync(lead);
        var confirmationTask = _emailService.SendWholesaleLeadConfirmationAsync(lead.Email, lead.FullName, lang);

        await Task.WhenAll(alertTask, confirmationTask);
    }

    public async Task<PagedResult<WholesaleLeadResponse>> GetAllAdminAsync(int pageNumber, int pageSize)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize   = Math.Clamp(pageSize, 1, 100);

        var query = _context.WholesaleLeads.OrderByDescending(l => l.CreatedAt);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new WholesaleLeadResponse
            {
                Id           = l.Id,
                FullName     = l.FullName,
                BusinessName = l.BusinessName,
                Email        = l.Email,
                Phone        = l.Phone,
                BusinessType  = l.BusinessType,
                MonthlyVolume = l.MonthlyVolume,
                Message      = l.Message,
                IsReviewed   = l.IsReviewed,
                CreatedAt    = l.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<WholesaleLeadResponse>(items, totalCount, pageNumber, pageSize, totalPages);
    }

    public async Task MarkAsReviewedAsync(Guid id)
    {
        var lead = await _context.WholesaleLeads.FindAsync(id)
            ?? throw new NotFoundException($"Wholesale lead {id} not found.");

        lead.IsReviewed = true;
        await _context.SaveChangesAsync();
    }
}
