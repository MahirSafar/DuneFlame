using DuneFlame.Application.DTOs.Basket;

namespace DuneFlame.Application.Interfaces;

public interface IBasketService
{
    Task<CustomerBasketDto> GetBasketAsync(string userId);
    Task UpdateBasketAsync(CustomerBasketDto basket, bool saveChanges = true);
    Task DeleteBasketAsync(string userId);
    Task RemoveItemFromBasketAsync(string userId, Guid itemId, bool saveChanges = true);
}
