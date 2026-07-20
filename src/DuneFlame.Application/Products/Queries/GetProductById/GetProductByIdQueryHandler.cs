using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Products.Queries.GetProductById;

public class GetProductByIdQueryHandler(IProductService productService)
    : IRequestHandler<GetProductByIdQuery, ProductResponse>
{
    public Task<ProductResponse> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
        => productService.GetByIdAsync(query.Id);
}
