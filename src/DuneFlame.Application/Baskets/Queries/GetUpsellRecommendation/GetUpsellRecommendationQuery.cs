using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Baskets.Queries.GetUpsellRecommendation;

public record GetUpsellRecommendationQuery(
    decimal Gap,
    List<Guid> ExcludedProductVariantIds,
    string CurrencyCode
) : IRequest<UpsellRecommendationDto?>;

public class GetUpsellRecommendationQueryHandler(IProductService productService)
    : IRequestHandler<GetUpsellRecommendationQuery, UpsellRecommendationDto?>
{
    public Task<UpsellRecommendationDto?> Handle(GetUpsellRecommendationQuery query, CancellationToken cancellationToken)
        => productService.GetUpsellRecommendationAsync(query.Gap, query.ExcludedProductVariantIds, query.CurrencyCode);
}
