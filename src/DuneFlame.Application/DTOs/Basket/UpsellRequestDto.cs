namespace DuneFlame.Application.DTOs.Basket;

public class UpsellRequestDto
{
    public decimal CurrentSubtotal { get; set; }
    public List<Guid> ExcludedProductVariantIds { get; set; } = new();
}