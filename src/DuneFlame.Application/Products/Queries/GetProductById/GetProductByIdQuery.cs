using DuneFlame.Application.DTOs.Product;
using MediatR;

namespace DuneFlame.Application.Products.Queries.GetProductById;

public record GetProductByIdQuery(Guid Id) : IRequest<ProductResponse>;
