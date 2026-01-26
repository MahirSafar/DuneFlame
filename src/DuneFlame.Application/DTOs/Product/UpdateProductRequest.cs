using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.DTOs.Product;

public record UpdateProductRequest(
    string Name,
    string Description,
    decimal StockInKg,
    Guid CategoryId,
    Guid? OriginId,
    List<Guid> RoastLevelIds,
    List<Guid> GrindTypeIds,
    List<ProductPriceCreateDto> Prices,
    List<IFormFile>? Images,
    List<Guid>? DeletedImageIds,
    Guid? SetMainImageId
);
