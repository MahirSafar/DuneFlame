using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Configuration;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.ShoppingContent.v2_1;
using Google.Apis.ShoppingContent.v2_1.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Alias to resolve ambiguity: our domain entity is 'Product';
// the Google Shopping Content SDK also has a 'Product' type in Data namespace.
using GProduct = Google.Apis.ShoppingContent.v2_1.Data.Product;
using DomainProduct = DuneFlame.Domain.Entities.Product;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Syncs DuneFlame products to Google Merchant Center via the Shopping Content API v2.1.
///
/// Authentication: Application Default Credentials (ADC).
/// On Cloud Run, attach the service account merchant-api-sync@duneflame.iam.gserviceaccount.com
/// to the revision — no key files, no secrets, no environment variables required.
/// Locally, run: gcloud auth application-default login
///
/// Each product is inserted with Upsert semantics: the Content API replaces the product
/// if the offerId already exists, so calling Sync twice is idempotent.
/// </summary>
public class GoogleMerchantService(
    IOptions<GoogleMerchantSettings> options,
    ILogger<GoogleMerchantService> logger) : IGoogleMerchantService
{
    private readonly GoogleMerchantSettings _settings = options.Value;


    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public async Task SyncProductToMerchantCenterAsync(
        DomainProduct product,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            logger.LogDebug("Google Merchant sync is disabled. Skipping sync for product {Slug}.", product.Slug);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.MerchantId))
        {
            logger.LogWarning("GoogleMerchant:MerchantId is not configured. Skipping Merchant Center sync.");
            return;
        }

        try
        {
            var service = await BuildShoppingContentServiceAsync();
            var merchantProduct = MapToMerchantProduct(product);
            var merchantId = ulong.Parse(_settings.MerchantId);

            // Content API v2.1 upsert — FeedId must NOT be set on the request;
            // ContentLanguage and TargetCountry are set on the product object itself.
            var request = service.Products.Insert(merchantProduct, merchantId);
            var result = await request.ExecuteAsync(cancellationToken);

            logger.LogInformation(
                "✅ UĞURLU | '{Slug}' → Merchant Center | Google ID: {GoogleProductId}",
                product.Slug,
                result.Id);
        }
        catch (Exception ex)
        {
            // Log and swallow — Merchant Center sync failure must never break the main
            // product save flow. The admin can re-trigger sync via the bulk endpoint.
            logger.LogError(ex,
                "❌ XƏTA | '{Slug}' → Merchant Center göndərilmədi. Səbəb: {Message}",
                product.Slug,
                ex.Message);
        }
    }

    public async Task DeleteProductFromMerchantCenterAsync(
        string productSlug,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled) return;
        if (string.IsNullOrWhiteSpace(_settings.MerchantId)) return;

        try
        {
            var service = await BuildShoppingContentServiceAsync();
            var merchantId = ulong.Parse(_settings.MerchantId);
            // Merchant Center product ID format: channel:contentLanguage:targetCountry:offerId
            var googleProductId = BuildGoogleProductId(productSlug);

            await service.Products.Delete(merchantId, googleProductId)
                         .ExecuteAsync(cancellationToken);

            logger.LogInformation(
                "Product '{Slug}' deleted from Merchant Center (ID: {GoogleProductId}).",
                productSlug, googleProductId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to delete product '{Slug}' from Google Merchant Center.",
                productSlug);
        }
    }

    public async Task BulkSyncProductsAsync(
        IEnumerable<DomainProduct> products,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
        {
            logger.LogInformation("Google Merchant sync is disabled. Bulk sync skipped.");
            return;
        }

        var productList = products.ToList();
        var total = productList.Count;

        logger.LogInformation(
            "=== Merchant Center Bulk Sync Başladı | MerchantId: {MerchantId} | Məhsul sayı: {Count} ===",
            _settings.MerchantId, total);

        var success = 0;
        var failed = 0;
        var index = 0;

        foreach (var product in productList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            try
            {
                await SyncProductToMerchantCenterAsync(product, cancellationToken);
                success++;
            }
            catch
            {
                failed++;
                // Exception already logged inside SyncProductToMerchantCenterAsync
            }

            // Progress log hər 10 məhsuldan bir
            if (index % 10 == 0 || index == total)
            {
                logger.LogInformation(
                    "📊 İrəliləyiş: {Index}/{Total} | ✅ Uğurlu: {Success} | ❌ Xəta: {Failed}",
                    index, total, success, failed);
            }

            // Content API limiti: 1 yazı/saniyə — throttle etmə
            await Task.Delay(1100, cancellationToken);
        }

        logger.LogInformation(
            "=== Merchant Center Bulk Sync Tamamlandı | ✅ Uğurlu: {Success} | ❌ Xəta: {Failed} | Cəmi: {Total} ===",
            success, failed, total);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds an authenticated ShoppingContentService using Application Default Credentials.
    /// On Cloud Run: automatically uses the service account attached to the revision.
    /// Locally: uses credentials from `gcloud auth application-default login`.
    /// </summary>
    private static async Task<ShoppingContentService> BuildShoppingContentServiceAsync()
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync();
        credential = credential.CreateScoped(ShoppingContentService.Scope.Content);

        return new ShoppingContentService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "DuneFlame-Backend"
        });
    }

    /// <summary>
    /// Maps a DuneFlame Product domain entity to a Google Merchant Center Product object.
    /// </summary>
    private GProduct MapToMerchantProduct(DomainProduct product)
    {
        // Prefer English translation; fall back to Arabic or first available
        var enTranslation = product.Translations.FirstOrDefault(t => t.LanguageCode == "en")
                         ?? product.Translations.FirstOrDefault();

        // Main product image
        var mainImage = product.Images.FirstOrDefault(i => i.IsMain)
                     ?? product.Images.FirstOrDefault();

        // Price: use the lowest-priced active variant in AED
        var lowestVariantPrice = product.Variants
            .Where(v => v.StockQuantity > 0)
            .OrderBy(v => v.Price)
            .FirstOrDefault()?.Price
            ?? product.Variants.FirstOrDefault()?.Price
            ?? 0m;

        // Availability
        var isAvailable = product.Variants.Any(v => v.StockQuantity > 0);

        // Product page URL: {storefront}/{locale}/product/{slug}
        var productLink = $"{_settings.StorefrontBaseUrl.TrimEnd('/')}/en/product/{product.Slug}";

        return new GProduct
        {
            // Required fields
            OfferId = product.Slug,             // Our slug = stable unique ID
            Title = enTranslation?.Name ?? product.Slug,
            Description = enTranslation?.MetaDescription  // Already clean (no HTML, 155 chars)
                          ?? StripBasicHtml(enTranslation?.Description ?? string.Empty),
            Link = productLink,
            ImageLink = mainImage?.ImageUrl ?? string.Empty,
            ContentLanguage = _settings.ContentLanguage,  // "en"
            TargetCountry = _settings.TargetCountry,      // "AE"
            Channel = "online",

            // Pricing
            Price = new Price
            {
                Value = lowestVariantPrice.ToString("F2"),
                Currency = "AED"
            },

            // Availability
            Availability = isAvailable ? "in stock" : "out of stock",

            // Optional but strongly recommended for Merchant Center quality score
            Brand = product.Translations.FirstOrDefault()?.Name  // Will be replaced by Brand entity below
                    ?? "DuneFlame",

            // Condition (required for new products)
            Condition = "new",

            // Google product category — general default; override per category if needed
            GoogleProductCategory = "Food, Beverages & Tobacco > Beverages > Coffee",

            // Additional image links (all non-main images)
            AdditionalImageLinks = product.Images
                .Where(i => !i.IsMain && !string.IsNullOrWhiteSpace(i.ImageUrl))
                .Select(i => i.ImageUrl)
                .ToList()
        };
    }

    // We use the already-clean MetaDescription field, but keep this as a last-resort fallback
    private static string StripBasicHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var plain = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ").Trim();
        return plain.Length > 5000 ? plain[..5000] : plain;  // Merchant Center max description length
    }

    /// <summary>
    /// Builds the full Google Product ID used for delete operations.
    /// Format required by Content API: {channel}:{contentLanguage}:{targetCountry}:{offerId}
    /// </summary>
    private string BuildGoogleProductId(string slug) =>
        $"online:{_settings.ContentLanguage}:{_settings.TargetCountry}:{slug}";
}
