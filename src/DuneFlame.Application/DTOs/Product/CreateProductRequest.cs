using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.DTOs.Product;

public record CreateProductRequest(
    string Name,
    string Description,
    decimal StockInKg,
    Guid CategoryId,
    Guid? OriginId,
    List<Guid> RoastLevelIds,
    List<Guid> GrindTypeIds,
    List<ProductPriceCreateDto> Prices,
    List<IFormFile>? Images
);

public record ProductPriceCreateDto(
    Guid ProductWeightId,
    decimal Price
);