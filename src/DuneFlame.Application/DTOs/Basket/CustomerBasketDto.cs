namespace DuneFlame.Application.DTOs.Basket;

public class CustomerBasketDto
{
    public string Id { get; set; } = string.Empty;
    public List<BasketItemDto> Items { get; set; } = [];
    public string? PaymentIntentId { get; set; }
    public string? ClientSecret { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public bool IsLocked { get; set; } = false;
}
