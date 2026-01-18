namespace DuneFlame.Application.DTOs.Basket;

public class CustomerBasketDto
{
    public string Id { get; set; } = string.Empty;
    public List<BasketItemDto> Items { get; set; } = [];
}
