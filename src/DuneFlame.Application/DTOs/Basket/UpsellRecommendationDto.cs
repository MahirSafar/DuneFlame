using System;

namespace DuneFlame.Application.DTOs.Basket;

public class UpsellRecommendationDto
{
    public Guid ProductId { get; set; }
    public Guid ProductPriceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string WeightLabel { get; set; } = string.Empty;
    public Dictionary<string, decimal> AvailablePrices { get; set; } = new();
}
