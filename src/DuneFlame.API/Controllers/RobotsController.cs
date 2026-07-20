using DuneFlame.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Serves robots.txt at the conventional path outside the /api/v1 prefix.
/// Anonymous — must never require authentication.
/// </summary>
[Route("robots.txt")]
[ApiController]
[AllowAnonymous]
public class RobotsController(IOptions<ClientUrls> clientUrls) : ControllerBase
{
    private readonly string _sitemapUrl =
        $"{clientUrls.Value.BaseUrl.TrimEnd('/')}/sitemap.xml";

    [HttpGet]
    public ContentResult GetRobots()
    {
        // Disallow the entire admin API surface from crawling.
        // Allow everything else explicitly so frontend routes are crawlable.
        var content = $"""
            User-agent: *
            Disallow: /api/v1/admin/
            Allow: /

            Sitemap: {_sitemapUrl}
            """;

        // Normalise indentation that the raw string literal introduces.
        content = string.Join("\n", content
            .Split('\n')
            .Select(l => l.TrimStart()));

        return Content(content, "text/plain");
    }
}
