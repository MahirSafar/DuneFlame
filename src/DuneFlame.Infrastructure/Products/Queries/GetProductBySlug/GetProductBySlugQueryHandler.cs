using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Products.Queries.GetProductBySlug;
using MediatR;

namespace DuneFlame.Infrastructure.Products.Queries.GetProductBySlug;

public class GetProductBySlugQueryHandler : IRequestHandler<GetProductBySlugQuery, ProductResponse>
{
    private readonly IProductService _productService;

    public GetProductBySlugQueryHandler(IProductService productService)
        => _productService = productService;

    public Task<ProductResponse> Handle(GetProductBySlugQuery query, CancellationToken cancellationToken)
        => _productService.GetBySlugAsync(query.Slug);
}
