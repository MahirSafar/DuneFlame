using DuneFlame.Application.DTOs.Cart;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class CartService(AppDbContext context) : ICartService
{
    private readonly AppDbContext _context = context;

    public async Task<CartDto> GetMyCartAsync(Guid userId)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.Product)
            .ThenInclude(p => p.Images)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null)
        {
            throw new NotFoundException($"Cart not found for user {userId}");
        }

        return MapToCartDto(cart);
    }

    public async Task<CartDto> AddToCartAsync(Guid userId, AddToCartRequest request)
    {
        var product = await _context.Products.FindAsync(request.ProductId);
        if (product == null)
        {
            throw new NotFoundException($"Product with ID {request.ProductId} not found");
        }

        if (product.StockQuantity < request.Quantity)
        {
            throw new BadRequestException($"Insufficient stock for product {product.Name}. Available: {product.StockQuantity}");
        }

        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null)
        {
            cart = new Cart { UserId = userId };
            _context.Carts.Add(cart);
            await _context.SaveChangesAsync();
        }

        var existingItem = cart.Items.FirstOrDefault(ci => ci.ProductId == request.ProductId);

        if (existingItem != null)
        {
            existingItem.Quantity += request.Quantity;
        }
        else
        {
            var cartItem = new CartItem
            {
                CartId = cart.Id,
                ProductId = request.ProductId,
                Quantity = request.Quantity
            };
            cart.Items.Add(cartItem);
        }

        await _context.SaveChangesAsync();

        cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.Product)
            .FirstAsync(c => c.Id == cart.Id);

        return MapToCartDto(cart);
    }

    public async Task<CartDto> RemoveFromCartAsync(Guid userId, Guid itemId)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.Product)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null)
        {
            throw new NotFoundException($"Cart not found for user {userId}");
        }

        var cartItem = cart.Items.FirstOrDefault(ci => ci.Id == itemId);
        if (cartItem == null)
        {
            throw new NotFoundException($"Cart item with ID {itemId} not found");
        }

        cart.Items.Remove(cartItem);
        await _context.SaveChangesAsync();

        return MapToCartDto(cart);
    }

    public async Task ClearCartAsync(Guid userId)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart != null)
        {
            cart.Items.Clear();
            await _context.SaveChangesAsync();
        }
    }

    private static CartDto MapToCartDto(Cart cart)
    {
        var totalAmount = cart.Items.Sum(ci => (ci.Product?.Price ?? 0) * ci.Quantity);

        var cartItems = cart.Items.Select(ci => new CartItemDto(
            ci.Id,
            ci.ProductId,
            ci.Product?.Name ?? "Unknown",
            ci.Product?.Price ?? 0,
            ci.Quantity,
            ci.Product?.Images.FirstOrDefault()?.ImageUrl
        )).ToList();

        return new CartDto(cart.Id, totalAmount, cartItems);
    }
}
