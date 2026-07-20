using DuneFlame.Application.DTOs.Seo;
using DuneFlame.Application.Interfaces;
using DuneFlame.Infrastructure.Configuration;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace DuneFlame.Infrastructure.Services;

public class SitemapService(
    AppDbContext db,
    IOptions<ClientUrls> clientUrls,
    HybridCache cache) : ISitemapService
{
    private static readonly string[] SupportedLocales = ["en", "ar"];
    private const string CacheKey = "sitemap:urls";
    private const string CacheTag = "sitemap";

    private readonly string _baseUrl = clientUrls.Value.BaseUrl.TrimEnd('/');

    public async Task<List<SitemapUrlDto>> GetSitemapUrlsAsync(CancellationToken cancellationToken = default)
    {
        // Cache the sitemap for 1 hour — it changes only when products/categories are updated.
        // Admin mutations should call cache.RemoveByTagAsync("sitemap") to invalidate.
        return await cache.GetOrCreateAsync(
            CacheKey,
            async (ct) => await BuildSitemapUrlsAsync(ct),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1) },
            tags: [CacheTag],
            cancellationToken: cancellationToken);
    }

    private async Task<List<SitemapUrlDto>> BuildSitemapUrlsAsync(CancellationToken cancellationToken)
    {
        var urls = new List<SitemapUrlDto>();

        // --- Static pages ---
        // Each static route gets one <url> per locale, with full hreflang cross-references.
        var staticRoutes = new[]
        {
            ("",          1.0m),
            ("about",     0.7m),
            ("contact",   0.6m),
            ("wholesale", 0.6m),
        };

        foreach (var (route, priority) in staticRoutes)
        {
            // Build the full URL for every locale first so we can cross-reference them.
            var localeUrls = SupportedLocales.ToDictionary(
                locale => locale,
                locale => string.IsNullOrEmpty(route)
                    ? $"{_baseUrl}/{locale}"
                    : $"{_baseUrl}/{locale}/{route}");

            foreach (var locale in SupportedLocales)
            {
                urls.Add(new SitemapUrlDto(
                    Loc: localeUrls[locale],
                    LastMod: DateTime.UtcNow.Date,
                    ChangeFreq: "monthly",
                    Priority: priority,
                    AlternateUrls: BuildAlternateUrls(localeUrls)
                ));
            }
        }

        // --- Active products ---
        var products = await db.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .Select(p => new { p.Slug, p.UpdatedAt, p.CreatedAt })
            .ToListAsync(cancellationToken);

        foreach (var product in products)
        {
            var lastMod = (product.UpdatedAt ?? product.CreatedAt).Date;

            var localeUrls = SupportedLocales.ToDictionary(
                locale => locale,
                locale => $"{_baseUrl}/{locale}/product/{product.Slug}");

            foreach (var locale in SupportedLocales)
            {
                urls.Add(new SitemapUrlDto(
                    Loc: localeUrls[locale],
                    LastMod: lastMod,
                    ChangeFreq: "weekly",
                    Priority: 0.8m,
                    AlternateUrls: BuildAlternateUrls(localeUrls)
                ));
            }
        }

        // --- Categories ---
        var categories = await db.Categories
            .AsNoTracking()
            .Select(c => new { c.Slug, c.UpdatedAt, c.CreatedAt })
            .ToListAsync(cancellationToken);

        foreach (var category in categories)
        {
            var lastMod = (category.UpdatedAt ?? category.CreatedAt).Date;

            var localeUrls = SupportedLocales.ToDictionary(
                locale => locale,
                locale => $"{_baseUrl}/{locale}/collection/{category.Slug}");

            foreach (var locale in SupportedLocales)
            {
                urls.Add(new SitemapUrlDto(
                    Loc: localeUrls[locale],
                    LastMod: lastMod,
                    ChangeFreq: "monthly",
                    Priority: 0.6m,
                    AlternateUrls: BuildAlternateUrls(localeUrls)
                ));
            }
        }

        return urls;
    }

    /// <summary>
    /// Builds the hreflang alternate URL map for a set of locale→URL pairs.
    /// Always includes an "x-default" entry pointing to the English URL.
    /// </summary>
    private static Dictionary<string, string> BuildAlternateUrls(Dictionary<string, string> localeUrls)
    {
        var alternates = new Dictionary<string, string>(localeUrls);

        // x-default signals to Google which URL to show for unrecognised locales.
        // We nominate English as the canonical default.
        if (localeUrls.TryGetValue("en", out var englishUrl))
            alternates["x-default"] = englishUrl;

        return alternates;
    }
}
