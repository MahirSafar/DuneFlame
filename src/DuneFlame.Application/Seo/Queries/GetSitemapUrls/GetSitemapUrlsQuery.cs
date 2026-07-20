using DuneFlame.Application.DTOs.Seo;
using MediatR;

namespace DuneFlame.Application.Seo.Queries.GetSitemapUrls;

public record GetSitemapUrlsQuery : IRequest<List<SitemapUrlDto>>;
