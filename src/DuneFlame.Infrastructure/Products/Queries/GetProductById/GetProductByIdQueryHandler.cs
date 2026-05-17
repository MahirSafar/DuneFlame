using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Products.Queries.GetProductById;
using MediatR;

namespace DuneFlame.Infrastructure.Products.Queries.GetProductById;

public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductResponse>
{
    private readonly IProductService _productService;

    public GetProductByIdQueryHandler(IProductService productService)
        => _productService = productService;

    public Task<ProductResponse> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
        => _productService.GetByIdAsync(query.Id);
}
