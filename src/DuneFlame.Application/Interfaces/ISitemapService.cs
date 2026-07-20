using DuneFlame.Application.DTOs.Seo;

namespace DuneFlame.Application.Interfaces;

public interface ISitemapService
{
    /// <summary>
    /// Returns all sitemap URL entries for active products and categories,
    /// including one entry per supported locale (en, ar).
    /// </summary>
    Task<List<SitemapUrlDto>> GetSitemapUrlsAsync(CancellationToken cancellationToken = default);
}
