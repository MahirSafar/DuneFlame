using DuneFlame.Application.DTOs.Seo;
using DuneFlame.Application.Seo.Queries.GetSitemapUrls;
using MediatR;
using System.Text;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Serves the XML sitemap at /sitemap.xml (Sitemaps Protocol 0.9).
/// Anonymous, intentionally outside the versioned /api/v1 route prefix.
/// </summary>
[Route("sitemap.xml")]
[ApiController]
[AllowAnonymous]
public class SitemapController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator;

    [HttpGet]
    public async Task<ContentResult> GetSitemap(CancellationToken cancellationToken)
    {
        var urls = await _mediator.Send(new GetSitemapUrlsQuery(), cancellationToken);
        var xml = BuildSitemapXml(urls);
        return Content(xml, "application/xml", Encoding.UTF8);
    }

    private static string BuildSitemapXml(List<SitemapUrlDto> urls)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"
        xmlns:xhtml="http://www.w3.org/1999/xhtml">
""".Trim());

        foreach (var url in urls)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{System.Security.SecurityElement.Escape(url.Loc)}</loc>");
            sb.AppendLine($"    <lastmod>{url.LastMod:yyyy-MM-dd}</lastmod>");
            sb.AppendLine($"    <changefreq>{url.ChangeFreq}</changefreq>");
            sb.AppendLine($"    <priority>{url.Priority:F1}</priority>");

            // --- hreflang alternate links ---
            // Only inject for localised URLs (those that contain a locale segment).
            // We derive the sibling URLs by toggling the locale prefix.
            if (url.AlternateUrls != null && url.AlternateUrls.Count > 0)
            {
                foreach (var alt in url.AlternateUrls)
                {
                    sb.AppendLine($"    <xhtml:link rel=\"alternate\" hreflang=\"{alt.Key}\" href=\"{System.Security.SecurityElement.Escape(alt.Value)}\"/>");
                }
            }

            sb.AppendLine("  </url>");
        }

        sb.Append("</urlset>");
        return sb.ToString();
    }
}
