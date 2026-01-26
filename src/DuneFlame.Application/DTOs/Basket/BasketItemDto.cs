using System.Text.Json.Serialization;

namespace DuneFlame.Application.DTOs.Basket;

public class BasketItemDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Guid ProductPriceId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string WeightLabel { get; set; } = string.Empty;
    public int Grams { get; set; }
    public string RoastLevelName { get; set; } = string.Empty;
    public string GrindTypeName { get; set; } = string.Empty;

    [JsonPropertyName("roastLevelId")]
    public Guid RoastLevelId { get; set; }

    [JsonPropertyName("grindTypeId")]
    public Guid GrindTypeId { get; set; }

    // Alias properties for frontend compatibility
    [JsonPropertyName("roastId")]
    public Guid RoastId 
    { 
        set => RoastLevelId = value; 
    }

    [JsonPropertyName("grindId")]
    public Guid GrindId 
    { 
        set => GrindTypeId = value; 
    }
}

