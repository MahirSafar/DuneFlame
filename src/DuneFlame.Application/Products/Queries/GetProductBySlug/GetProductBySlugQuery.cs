using DuneFlame.Application.DTOs.Product;
using MediatR;

namespace DuneFlame.Application.Products.Queries.GetProductBySlug;

public record GetProductBySlugQuery(string Slug) : IRequest<ProductResponse>;
