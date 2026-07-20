using DuneFlame.Application.Common;
using DuneFlame.Application.Interfaces;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.API.Controllers;

/// <summary>
/// SEO data backfill + Google Merchant Center sync utilities.
/// All actions are idempotent — safe to run multiple times.
/// </summary>
[Route("api/v1/admin/seo")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminSeoController(
    AppDbContext db,
    IServiceScopeFactory scopeFactory,
    ILogger<AdminSeoController> logger) : ControllerBase
{
    // =========================================================================
    // Google Merchant Center Sync
    // =========================================================================

    /// <summary>
    /// POST /api/v1/admin/seo/sync-all
    ///
    /// Bütün aktiv məhsulları Google Merchant Center-ə göndərir.
    /// Data Source: DuneFlame-API-Sync (ID: konfiqurasiyadan oxunur).
    ///
    /// Dərhal 202 Accepted qaytarır — sync arxa planda gedir.
    /// 74 məhsul üçün ~82 saniyə çəkir (Content API: 1 yazı/saniyə limiti).
    /// Nəticəni server loglarında izləyin.
    /// </summary>
    [HttpPost("sync-all")]
    public async Task<IActionResult> SyncAllProductsToGoogleAsync(CancellationToken cancellationToken)
    {
        // Yalnız ID-ləri çək — background job tam məhsulları yükləyəcək
        var productIds = await db.Products
            .Where(p => !p.IsDeleted)
            .Select(p => new { p.Id, p.Slug })
            .ToListAsync(cancellationToken);

        if (productIds.Count == 0)
            return Ok(new
            {
                message = "Aktiv məhsul tapılmadı.",
                productCount = 0
            });

        logger.LogInformation(
            "[Merchant Sync] {Count} aktiv məhsul növbəyə alındı. Background sync başladı.",
            productIds.Count);

        // Background-da işlət — HTTP cavabını gözlətmə
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var bgDb      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bgMerchant = scope.ServiceProvider.GetRequiredService<IGoogleMerchantService>();
            var bgLogger   = scope.ServiceProvider.GetRequiredService<ILogger<AdminSeoController>>();

            var ids = productIds.Select(p => p.Id).ToList();

            // Tam navigasiya xüsusiyyətləri ilə yüklə
            var products = await bgDb.Products
                .Include(p => p.Translations)
                .Include(p => p.Images)
                .Include(p => p.Variants)
                .Where(p => ids.Contains(p.Id))
                .AsNoTracking()
                .ToListAsync();

            bgLogger.LogInformation(
                "[Merchant Sync] {Count} məhsul yükləndi. Google-a göndərilir...", products.Count);

            await bgMerchant.BulkSyncProductsAsync(products, CancellationToken.None);

            bgLogger.LogInformation(
                "[Merchant Sync] ✅ Tamamlandı — {Count} məhsul Google Merchant Center-ə göndərildi.",
                products.Count);
        });

        return Accepted(new
        {
            message = $"{productIds.Count} məhsul arxa planda Google Merchant Center-ə göndərilir.",
            productCount = productIds.Count,
            dataSource = "DuneFlame-API-Sync",
            estimatedDurationSeconds = productIds.Count * 1.1,
            logNote = "Server loglarında '✅ Tamamlandı' mesajını gözləyin."
        });
    }

    // =========================================================================
    // SEO Backfill
    // =========================================================================

    /// <summary>
    /// POST /api/v1/admin/seo/backfill-products
    ///
    /// Iterates all products and auto-generates SEO fields where they are null:
    ///   - ProductTranslation.MetaTitle
    ///   - ProductTranslation.MetaDescription
    ///   - ProductImage.AltText
    ///
    /// Fully idempotent — rows that already have values are skipped.
    /// Returns a summary of exactly how many rows were written.
    /// </summary>
    [HttpPost("backfill-products")]
    public async Task<IActionResult> BackfillProductSeoAsync(CancellationToken cancellationToken)
    {
        var products = await db.Products
            .Include(p => p.Translations)
            .Include(p => p.Images)
            .ToListAsync(cancellationToken);

        int translationsUpdated = 0;
        int imagesUpdated = 0;

        foreach (var product in products)
        {
            var enName = product.Translations.FirstOrDefault(t => t.LanguageCode == "en")?.Name
                         ?? product.Slug;

            foreach (var translation in product.Translations)
            {
                var changed = false;

                if (translation.MetaTitle is null)
                {
                    translation.MetaTitle = SeoGenerator.GenerateMetaTitle(translation.Name, translation.LanguageCode);
                    changed = true;
                }

                if (translation.MetaDescription is null)
                {
                    translation.MetaDescription = SeoGenerator.GenerateMetaDescription(translation.Description);
                    changed = true;
                }

                if (changed) translationsUpdated++;
            }

            foreach (var image in product.Images)
            {
                if (image.AltText is null)
                {
                    image.AltText = SeoGenerator.GenerateAltText(enName);
                    imagesUpdated++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "SEO backfill completed successfully.",
            productsScanned = products.Count,
            translationRowsUpdated = translationsUpdated,
            imageRowsUpdated = imagesUpdated,
            totalRowsWritten = translationsUpdated + imagesUpdated
        });
    }
}
