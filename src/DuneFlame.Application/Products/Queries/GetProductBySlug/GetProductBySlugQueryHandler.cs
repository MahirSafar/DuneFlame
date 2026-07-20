using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Products.Queries.GetProductBySlug;

public class GetProductBySlugQueryHandler(IProductService productService)
    : IRequestHandler<GetProductBySlugQuery, ProductResponse>
{
    public Task<ProductResponse> Handle(GetProductBySlugQuery query, CancellationToken cancellationToken)
        => productService.GetBySlugAsync(query.Slug);
}
