using DuneFlame.Application.Interfaces;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Admin utilities for Google Merchant Center synchronisation.
/// </summary>
[Route("api/v1/admin/merchant")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminMerchantController(
    IGoogleMerchantService merchantService,
    AppDbContext db,
    IServiceScopeFactory scopeFactory,
    ILogger<AdminMerchantController> logger) : ControllerBase
{
    /// <summary>
    /// POST /api/v1/admin/merchant/sync-all
    ///
    /// Bütün aktiv məhsulları Google Merchant Center-ə göndərir.
    /// Dərhal 202 Accepted qaytarır — sync arxa planda gedir.
    /// 74 məhsul üçün ~82 saniyə çəkir (Content API: 1 yazı/saniyə limiti).
    /// Nəticəni server loglarında izləyə bilərsiniz.
    /// </summary>
    [HttpPost("sync-all")]
    public async Task<IActionResult> SyncAllProductsAsync(CancellationToken cancellationToken)
    {
        // Load product IDs only — the background job will reload with full navigation properties
        var productIds = await db.Products
            .Where(p => !p.IsDeleted)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (productIds.Count == 0)
            return Ok(new { message = "No active products found to sync.", productCount = 0 });

        logger.LogInformation(
            "Merchant Center bulk sync started. {Count} products queued.", productIds.Count);

        // Fire-and-forget: run in background so HTTP response returns immediately.
        // Uses IServiceScopeFactory to create a new DI scope (the request scope ends after return).
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var bgDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bgMerchant = scope.ServiceProvider.GetRequiredService<IGoogleMerchantService>();
            var bgLogger = scope.ServiceProvider.GetRequiredService<ILogger<AdminMerchantController>>();

            var products = await bgDb.Products
                .Include(p => p.Translations)
                .Include(p => p.Images)
                .Include(p => p.Variants)
                .Where(p => productIds.Contains(p.Id))
                .AsNoTracking()
                .ToListAsync();

            bgLogger.LogInformation(
                "Background Merchant sync: loaded {Count} products, starting upload.", products.Count);

            await bgMerchant.BulkSyncProductsAsync(products, CancellationToken.None);

            bgLogger.LogInformation(
                "Background Merchant sync: ALL {Count} products sent to Merchant Center.", products.Count);
        });

        return Accepted(new
        {
            message = $"{productIds.Count} məhsul arxa planda Google-a göndərilir. Server loglarını izləyin.",
            productCount = productIds.Count,
            estimatedSeconds = productIds.Count * 1.1,
            tip = "Nəticəni yoxlamaq üçün: GET /api/v1/admin/merchant/status"
        });
    }

    /// <summary>
    /// POST /api/v1/admin/merchant/sync/{slug}
    ///
    /// Tək məhsulu slug-a görə dərhal Google-a göndərir (sinxron — tez cavab verir).
    /// </summary>
    [HttpPost("sync/{slug}")]
    public async Task<IActionResult> SyncSingleProductAsync(string slug, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Include(p => p.Translations)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == slug, cancellationToken);

        if (product is null)
            return NotFound(new { message = $"Slug '{slug}' ilə məhsul tapılmadı." });

        await merchantService.SyncProductToMerchantCenterAsync(product, cancellationToken);

        return Ok(new
        {
            message = $"'{slug}' məhsulu uğurla Google Merchant Center-ə göndərildi.",
            slug
        });
    }

    /// <summary>
    /// DELETE /api/v1/admin/merchant/product/{slug}
    ///
    /// Merchant Center-dən məhsulu silir (deaktiv edildikdə və ya silinəndə çağırın).
    /// </summary>
    [HttpDelete("product/{slug}")]
    public async Task<IActionResult> DeleteFromMerchantAsync(string slug, CancellationToken cancellationToken)
    {
        await merchantService.DeleteProductFromMerchantCenterAsync(slug, cancellationToken);
        return Ok(new { message = $"'{slug}' Merchant Center-dən silmə sorğusu göndərildi." });
    }

    /// <summary>
    /// GET /api/v1/admin/merchant/status
    ///
    /// Merchant Center konfiqurasiyasının vəziyyətini yoxlayır.
    /// Sync işlətməzdən əvvəl bunu çağırın.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        var totalActive = await db.Products.CountAsync(p => !p.IsDeleted, cancellationToken);
        var totalAll = await db.Products.CountAsync(cancellationToken);

        return Ok(new
        {
            activeProducts = totalActive,
            inactiveProducts = totalAll - totalActive,
            estimatedSyncDurationSeconds = totalActive * 1.1,
            instructions = new[]
            {
                "1. appsettings.json-da 'Enabled: true' et",
                "2. MerchantId-ni doldur",
                "3. POST /api/v1/admin/merchant/sync-all çağır",
                "4. Server loglarında '[INF] Background Merchant sync: ALL ... products sent' mesajını gözlə"
            }
        });
    }
}
