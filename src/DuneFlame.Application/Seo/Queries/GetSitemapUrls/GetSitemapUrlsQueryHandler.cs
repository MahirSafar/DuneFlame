using DuneFlame.Application.DTOs.Seo;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Seo.Queries.GetSitemapUrls;

public class GetSitemapUrlsQueryHandler(ISitemapService sitemapService)
    : IRequestHandler<GetSitemapUrlsQuery, List<SitemapUrlDto>>
{
    public Task<List<SitemapUrlDto>> Handle(GetSitemapUrlsQuery query, CancellationToken cancellationToken)
        => sitemapService.GetSitemapUrlsAsync(cancellationToken);
}
