using DuneFlame.Application.DTOs.Cart;

namespace DuneFlame.Application.Interfaces;

public interface ICartService
{
    Task<CartDto> GetMyCartAsync(Guid userId);
    Task<CartDto> AddToCartAsync(Guid userId, AddToCartRequest request);
    Task<CartDto> RemoveFromCartAsync(Guid userId, Guid itemId);
    Task ClearCartAsync(Guid userId);
}
