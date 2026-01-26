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
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Product)
            .ThenInclude(p => p.Images)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Product)
            .ThenInclude(p => p.RoastLevels)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Product)
            .ThenInclude(p => p.GrindTypes)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Weight)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.RoastLevel)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.GrindType)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.UserId == userId) ?? throw new NotFoundException($"Cart not found for user {userId}");
        return MapToCartDto(cart);
    }

    public async Task<CartDto> AddToCartAsync(Guid userId, AddToCartRequest request)
    {
        // Get ProductPrice with related Weight data
        var productPrice = await _context.ProductPrices
            .Include(pp => pp.Product)
            .Include(pp => pp.Weight)
            .FirstOrDefaultAsync(pp => pp.Id == request.ProductPriceId) ?? throw new NotFoundException($"ProductPrice with ID {request.ProductPriceId} not found");
        var product = productPrice.Product ?? throw new NotFoundException($"Product for ProductPrice {request.ProductPriceId} not found");

        // Calculate total weight needed in KG
        decimal totalWeightKg = request.Quantity * (productPrice.Weight!.Grams / 1000m);

        // Check stock availability
        if (product.StockInKg < totalWeightKg)
        {
            throw new BadRequestException($"Insufficient stock for product {product.Name}. Available: {product.StockInKg}kg, Requested: {totalWeightKg}kg");
        }

        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null)
        {
            cart = new Cart { UserId = userId };
            _context.Carts.Add(cart);
            await _context.SaveChangesAsync();
        }

        // Normalize empty GUIDs to enable proper matching (Guid.Empty means no selection)
        var roastLevelId = request.RoastLevelId == Guid.Empty ? Guid.Empty : request.RoastLevelId;
        var grindTypeId = request.GrindTypeId == Guid.Empty ? Guid.Empty : request.GrindTypeId;

        var existingItem = cart.Items.FirstOrDefault(ci => 
            ci.ProductPriceId == request.ProductPriceId && 
            ci.RoastLevelId == roastLevelId &&
            ci.GrindTypeId == grindTypeId);

        if (existingItem != null)
        {
            existingItem.Quantity += request.Quantity;
        }
        else
        {
            var cartItem = new CartItem
            {
                CartId = cart.Id,
                ProductPriceId = request.ProductPriceId,
                RoastLevelId = roastLevelId,
                GrindTypeId = grindTypeId,
                Quantity = request.Quantity
            };
            cart.Items.Add(cartItem);
        }

            await _context.SaveChangesAsync();

             cart = await _context.Carts
                 .Include(c => c.Items)
                 .ThenInclude(ci => ci.ProductPrice)
                 .ThenInclude(pp => pp.Product)
                 .ThenInclude(p => p.Images)
                 .Include(c => c.Items)
                 .ThenInclude(ci => ci.ProductPrice)
                 .ThenInclude(pp => pp.Product)
                 .ThenInclude(p => p.RoastLevels)
                 .Include(c => c.Items)
                 .ThenInclude(ci => ci.ProductPrice)
                 .ThenInclude(pp => pp.Product)
                 .ThenInclude(p => p.GrindTypes)
                 .Include(c => c.Items)
                 .ThenInclude(ci => ci.ProductPrice)
                 .ThenInclude(pp => pp.Weight)
                 .Include(c => c.Items)
                 .ThenInclude(ci => ci.RoastLevel)
                 .Include(c => c.Items)
                 .ThenInclude(ci => ci.GrindType)
                 .AsSplitQuery()
                 .FirstAsync(c => c.Id == cart.Id);

            return MapToCartDto(cart);
        }

    public async Task<CartDto> RemoveFromCartAsync(Guid userId, Guid itemId)
    {
        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Product)
            .ThenInclude(p => p.Images)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Product)
            .ThenInclude(p => p.RoastLevels)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Product)
            .ThenInclude(p => p.GrindTypes)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Weight)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.RoastLevel)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.GrindType)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.UserId == userId) ?? throw new NotFoundException($"Cart not found for user {userId}");
        var cartItem = cart.Items.FirstOrDefault(ci => ci.Id == itemId) ?? throw new NotFoundException($"Cart item with ID {itemId} not found");
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

    /// <summary>
    /// Bulk sync cart items from frontend. Groups items by ProductPriceId + RoastLevelId + GrindTypeId
    /// and sums quantities to prevent duplicate rows. This is the "stacking" fix for bulk updates.
    /// </summary>
    public async Task<CartDto> SyncCartItemsAsync(Guid userId, List<AddToCartRequest> items)
    {
        // Get or create cart
        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cart == null)
        {
            cart = new Cart { UserId = userId };
            _context.Carts.Add(cart);
            await _context.SaveChangesAsync();
        }

        // Clear existing items
        cart.Items.Clear();

        // Normalize items: convert Guid.Empty to null for consistent grouping
        var normalizedItems = items
            .Select(item => new AddToCartRequest(
                item.ProductPriceId,
                item.RoastLevelId == Guid.Empty ? Guid.Empty : item.RoastLevelId,
                item.GrindTypeId == Guid.Empty ? Guid.Empty : item.GrindTypeId,
                item.Quantity
            ))
            .ToList();

        // Group items by variation key (ProductPriceId + RoastLevelId + GrindTypeId)
        // Normalizing Guid.Empty ensures consistent grouping across requests
        // Sum quantities for duplicates
        var groupedItems = normalizedItems
            .GroupBy(item => new
            {
                item.ProductPriceId,
                item.RoastLevelId,
                item.GrindTypeId
            })
            .Select(group => new AddToCartRequest(
                group.Key.ProductPriceId,
                group.Key.RoastLevelId,
                group.Key.GrindTypeId,
                group.Sum(item => item.Quantity)
            ))
            .ToList();

        // Validate and create CartItems
        foreach (var item in groupedItems)
        {
            // Verify ProductPrice exists
            var productPrice = await _context.ProductPrices
                .Include(pp => pp.Product)
                .Include(pp => pp.Weight)
                .FirstOrDefaultAsync(pp => pp.Id == item.ProductPriceId);

            if (productPrice == null)
            {
                throw new NotFoundException($"ProductPrice with ID {item.ProductPriceId} not found");
            }

            var product = productPrice.Product ?? throw new NotFoundException($"Product for ProductPrice {item.ProductPriceId} not found");

            // Check stock availability
            decimal totalWeightKg = item.Quantity * (productPrice.Weight!.Grams / 1000m);
            if (product.StockInKg < totalWeightKg)
            {
                throw new BadRequestException($"Insufficient stock for product {product.Name}. Available: {product.StockInKg}kg, Requested: {totalWeightKg}kg");
            }

            // Create CartItem with deduplicated quantity
            var cartItem = new CartItem
            {
                CartId = cart.Id,
                ProductPriceId = item.ProductPriceId,
                RoastLevelId = item.RoastLevelId,
                GrindTypeId = item.GrindTypeId,
                Quantity = item.Quantity
            };

            cart.Items.Add(cartItem);
        }

        await _context.SaveChangesAsync();

        // Reload cart with all relationships for response
        cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Product)
            .ThenInclude(p => p.Images)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Product)
            .ThenInclude(p => p.RoastLevels)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Product)
            .ThenInclude(p => p.GrindTypes)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductPrice)
            .ThenInclude(pp => pp.Weight)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.RoastLevel)
            .Include(c => c.Items)
            .ThenInclude(ci => ci.GrindType)
            .FirstAsync(c => c.Id == cart.Id);

        return MapToCartDto(cart);
    }

    private static CartDto MapToCartDto(Cart cart)
    {
        var totalAmount = cart.Items.Sum(ci => 
        {
            var productPrice = ci.ProductPrice;
            if (productPrice == null) return 0;
            return productPrice.Price * ci.Quantity;
        });

        var cartItems = cart.Items.Select(ci => 
        {
            var productPrice = ci.ProductPrice;
            var product = productPrice?.Product;
            var price = productPrice?.Price ?? 0;
            var weight = productPrice?.Weight;

            // Use the specific RoastLevel and GrindType stored in CartItem, not just the first from product
            var roastLevelName = ci.RoastLevel?.Name ?? "Unknown";
            var grindTypeName = ci.GrindType?.Name ?? "Unknown";

            return new CartItemDto(
                ci.Id,
                product?.Id ?? Guid.Empty,
                productPrice?.Id ?? Guid.Empty,
                product?.Name ?? "Unknown",
                price,
                ci.Quantity,
                product?.Images.FirstOrDefault()?.ImageUrl,
                weight?.Label ?? "Unknown",
                weight?.Grams ?? 0,
                roastLevelName,
                grindTypeName,
                ci.RoastLevelId,
                ci.GrindTypeId
            );
        }).ToList();

        return new CartDto(cart.Id, totalAmount, cartItems);
    }
}

