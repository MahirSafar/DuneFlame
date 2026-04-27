using DuneFlame.Application.DTOs.Product;

namespace DuneFlame.Application.DTOs.Basket;

public class UpsellRecommendationDto
{
    public Guid ProductId { get; set; }
    public Guid ProductVariantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string WeightLabel { get; set; } = string.Empty;
    public Dictionary<string, decimal> AvailablePrices { get; set; } = new();

    public bool HasVariants { get; set; }
    public bool IsCoffee { get; set; }
    public string? OriginName { get; set; }
    public List<string>? RoastLevelNames { get; set; }
    public List<string>? GrindTypeNames { get; set; }
    public List<VariantOptionDto>? Options { get; set; }
}
