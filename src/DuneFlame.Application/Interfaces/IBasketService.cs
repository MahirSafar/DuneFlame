using DuneFlame.Application.DTOs.Basket;

namespace DuneFlame.Application.Interfaces;

public interface IBasketService
{
    Task<CustomerBasketDto> GetBasketAsync(string userId);
    Task UpdateBasketAsync(CustomerBasketDto basket);
    Task DeleteBasketAsync(string userId);
    Task RemoveItemFromBasketAsync(string userId, Guid itemId);
}
