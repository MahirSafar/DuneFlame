using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using MediatR;

namespace DuneFlame.Application.Products.Queries.GetAllProducts;

/// <summary>Query for public product listing (paginated, localized).</summary>
public record GetAllProductsQuery(
    int PageNumber = 1,
    int PageSize = 8,
    string? SortBy = null,
    string? Search = null,
    Guid? CategoryId = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    Guid? BrandId = null,
    Guid[]? RoastLevelIds = null,
    Guid[]? OriginIds = null,
    bool AdminView = false
) : IRequest<PagedResult<ProductResponse>>;
