using Microsoft.AspNetCore.Http;

namespace DuneFlame.Application.DTOs.Product;

public record CreateProductRequest(
    string Name,
    string Description,
    decimal Price,
    int StockQuantity,
    Guid CategoryId,
    List<IFormFile>? Images // Şəkillər buradan gəlir
);