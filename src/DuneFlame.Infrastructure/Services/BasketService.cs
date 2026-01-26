using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Services;

public class BasketService(IDistributedCache cache) : IBasketService
{
    private readonly IDistributedCache _cache = cache;

    public async Task<CustomerBasketDto> GetBasketAsync(string userId)
    {
        var basketJson = await _cache.GetStringAsync(userId);
        
        if (string.IsNullOrEmpty(basketJson))
        {
            return new CustomerBasketDto { Id = userId, Items = [] };
        }

        return JsonSerializer.Deserialize<CustomerBasketDto>(basketJson) 
            ?? new CustomerBasketDto { Id = userId, Items = [] };
    }

    public async Task UpdateBasketAsync(CustomerBasketDto basket)
    {
        var basketJson = JsonSerializer.Serialize(basket);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddDays(30)
        };
        
        await _cache.SetStringAsync(basket.Id, basketJson, options);
    }

        public async Task DeleteBasketAsync(string userId)
        {
            await _cache.RemoveAsync(userId);
        }

        public async Task RemoveItemFromBasketAsync(string userId, Guid itemId)
        {
            if (userId == null || string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
            }

            if (itemId == Guid.Empty)
            {
                throw new ArgumentException("Item ID cannot be empty GUID.", nameof(itemId));
            }

            var basket = await GetBasketAsync(userId);

            if (basket?.Items == null || basket.Items.Count == 0)
            {
                throw new DuneFlame.Domain.Exceptions.NotFoundException("Basket is empty or not found");
            }

            var itemToRemove = basket.Items.FirstOrDefault(x => x.Id == itemId);
            if (itemToRemove == null)
            {
                throw new DuneFlame.Domain.Exceptions.NotFoundException($"Basket item with ID {itemId} not found");
            }

            basket.Items.Remove(itemToRemove);
            await UpdateBasketAsync(basket);
        }
    }
