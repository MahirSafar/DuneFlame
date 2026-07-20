using DuneFlame.Application.DTOs.Common;
using DuneFlame.Application.DTOs.Product;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Products.Queries.GetAllProducts;

public class GetAllProductsQueryHandler(IProductService productService)
    : IRequestHandler<GetAllProductsQuery, PagedResult<ProductResponse>>
{
    public Task<PagedResult<ProductResponse>> Handle(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        if (query.AdminView)
        {
            return productService.GetAllAdminAsync(
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

        return productService.GetAllAsync(
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
