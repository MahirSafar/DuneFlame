using System.Text.Json.Serialization;

namespace DuneFlame.Application.DTOs.Cart;

public class AddToCartRequest
{
    [JsonPropertyName("productVariantId")]
    public Guid ProductVariantId { get; set; }

    [JsonPropertyName("roastLevelId")]
    public Guid? RoastLevelId { get; set; }

    [JsonPropertyName("grindTypeId")]
    public Guid? GrindTypeId { get; set; }

    public int Quantity { get; set; }

    // Alias properties for frontend compatibility - set main properties when alternative names are used
    [JsonPropertyName("variantId")]
    public Guid VariantId 
    { 
        set => ProductVariantId = value; 
    }

    [JsonPropertyName("roastId")]
    public Guid? RoastId 
    { 
        set => RoastLevelId = value; 
    }

    [JsonPropertyName("grindId")]
    public Guid? GrindId 
    { 
        set => GrindTypeId = value; 
    }

    public AddToCartRequest(Guid productVariantId, Guid? roastLevelId, Guid? grindTypeId, int quantity)
    {
        ProductVariantId = productVariantId;
        RoastLevelId = roastLevelId;
        GrindTypeId = grindTypeId;
        Quantity = quantity;
    }

    public AddToCartRequest() { }
}
