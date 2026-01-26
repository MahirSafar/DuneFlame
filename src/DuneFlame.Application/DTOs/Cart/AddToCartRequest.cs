using System.Text.Json.Serialization;

namespace DuneFlame.Application.DTOs.Cart;

public class AddToCartRequest
{
    [JsonPropertyName("productPriceId")]
    public Guid ProductPriceId { get; set; }

    [JsonPropertyName("roastLevelId")]
    public Guid RoastLevelId { get; set; }

    [JsonPropertyName("grindTypeId")]
    public Guid GrindTypeId { get; set; }

    public int Quantity { get; set; }

    // Alias properties for frontend compatibility - set main properties when alternative names are used
    [JsonPropertyName("priceId")]
    public Guid PriceId 
    { 
        set => ProductPriceId = value; 
    }

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

    public AddToCartRequest(Guid productPriceId, Guid roastLevelId, Guid grindTypeId, int quantity)
    {
        ProductPriceId = productPriceId;
        RoastLevelId = roastLevelId;
        GrindTypeId = grindTypeId;
        Quantity = quantity;
    }

    public AddToCartRequest() { }
}
