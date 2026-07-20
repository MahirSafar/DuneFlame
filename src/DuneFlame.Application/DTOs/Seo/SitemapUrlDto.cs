namespace DuneFlame.Application.DTOs.Seo;

/// <summary>
/// Represents a single &lt;url&gt; entry in a Sitemaps Protocol 0.9 document.
/// </summary>
/// <param name="AlternateUrls">
/// Optional hreflang alternates: key = hreflang value (e.g. "en", "ar", "x-default"),
/// value = absolute URL. When populated, the sitemap emits xhtml:link alternate tags.
/// </param>
public record SitemapUrlDto(
    string Loc,
    DateTime LastMod,
    string ChangeFreq,
    decimal Priority,
    Dictionary<string, string>? AlternateUrls = null
);
