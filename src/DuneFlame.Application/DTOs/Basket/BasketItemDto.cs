using System.Text.Json.Serialization;

namespace DuneFlame.Application.DTOs.Basket;

public class BasketItemDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Guid ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public List<string> Attributes { get; set; } = new();
    public string? RoastLevelName { get; set; } = string.Empty;
    public string? GrindTypeName { get; set; } = string.Empty;
    public Guid? RoastLevelId { get; set; }
    public Guid? GrindTypeId { get; set; }
}

