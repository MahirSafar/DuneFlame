using DuneFlame.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.DTOs.Product;

public record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    decimal DiscountPercentage,
    int StockQuantity,
    Guid CategoryId,
    Guid? OriginId,
    RoastLevel RoastLevel,
    int Weight,
    string FlavorNotes,
    List<IFormFile>? Images
);