using DuneFlame.Application.DTOs.Basket;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Services;

public class BasketService(AppDbContext context) : IBasketService
{
    private readonly AppDbContext _context = context;

    public async Task<CustomerBasketDto> GetBasketAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }

        var basketEntity = await _context.CustomerBaskets.FirstOrDefaultAsync(b => b.Id == userId);

        if (basketEntity == null || string.IsNullOrEmpty(basketEntity.Items) || basketEntity.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return new CustomerBasketDto { Id = userId, Items = [] };
        }

        var options = new JsonSerializerOptions { 
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        };
        var items = JsonSerializer.Deserialize<List<BasketItemDto>>(basketEntity.Items, options) ?? [];
        return new CustomerBasketDto { Id = userId, Items = items };
    }

    public async Task UpdateBasketAsync(CustomerBasketDto basket, bool saveChanges = true)
    {
        if (basket == null || string.IsNullOrWhiteSpace(basket.Id))
        {
            throw new ArgumentException("Basket and Basket.Id cannot be null or empty.", nameof(basket));
        }

        var options = new JsonSerializerOptions { 
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
        };
        var itemsJson = JsonSerializer.Serialize(basket.Items ?? [], options);
        var basketEntity = await _context.CustomerBaskets.FirstOrDefaultAsync(b => b.Id == basket.Id);

        if (basketEntity == null)
        {
            basketEntity = new CustomerBasket
            {
                Id = basket.Id,
                Items = itemsJson,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(3)
            };
            _context.CustomerBaskets.Add(basketEntity);
        }
        else
        {
            basketEntity.Items = itemsJson;
            basketEntity.ExpiresAt = DateTimeOffset.UtcNow.AddDays(3);
            _context.CustomerBaskets.Update(basketEntity);
        }

        if (saveChanges) { await _context.SaveChangesAsync(); }
    }

    public async Task DeleteBasketAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }

        var basketEntity = await _context.CustomerBaskets.FirstOrDefaultAsync(b => b.Id == userId);
        if (basketEntity != null)
        {
            _context.CustomerBaskets.Remove(basketEntity);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RemoveItemFromBasketAsync(string userId, Guid itemId, bool saveChanges = true)
    {
        if (string.IsNullOrWhiteSpace(userId))
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
        await UpdateBasketAsync(basket, saveChanges);
    }
}
