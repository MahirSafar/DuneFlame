using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using DuneFlame.Application.Products.Queries.GetAllProducts;
using MediatR;

namespace DuneFlame.Infrastructure.Products.Queries.GetAllProducts;

public class GetAllProductsQueryHandler : IRequestHandler<GetAllProductsQuery, PagedResult<ProductResponse>>
{
    private readonly IProductService _productService;

    public GetAllProductsQueryHandler(IProductService productService)
        => _productService = productService;

    public Task<PagedResult<ProductResponse>> Handle(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        if (query.AdminView)
        {
            return _productService.GetAllAdminAsync(
                pageNumber: query.PageNumber,
                pageSize: query.PageSize,
                sortBy: query.SortBy,
                search: query.Search,
                categoryId: query.CategoryId,
                minPrice: query.MinPrice,
                maxPrice: query.MaxPrice,
                brandId: query.BrandId,
                roastLevelIds: query.RoastLevelIds,
                originIds: query.OriginIds);
        }

        return _productService.GetAllAsync(
            pageNumber: query.PageNumber,
            pageSize: query.PageSize,
            sortBy: query.SortBy,
            search: query.Search,
            categoryId: query.CategoryId,
            minPrice: query.MinPrice,
            maxPrice: query.MaxPrice,
            brandId: query.BrandId,
            roastLevelIds: query.RoastLevelIds,
            originIds: query.OriginIds);
    }
}
