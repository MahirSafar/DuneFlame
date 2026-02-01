using DuneFlame.Application.DTOs.Cart;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class CartService(AppDbContext context, IHttpContextAccessor httpContextAccessor) : ICartService
{
    private readonly AppDbContext _context = context;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    /// <summary>
    /// Extracts language code from Accept-Language header.
    /// Defaults to "en" (English) if header is missing or language is not supported.
    /// </summary>
    private string GetLanguageCode()
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
                return "en";

            var header = httpContext.Request.Headers["Accept-Language"].ToString();
            if (string.IsNullOrWhiteSpace(header))
                return "en";

            var langPart = header.Split(',')[0].Trim();
            var lang = langPart.Length >= 2 ? langPart.Substring(0, 2).ToLower() : "en";
            return lang == "ar" ? "ar" : "en";
        }
        catch
        {
            return "en";
        }
    }

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
                return MapToCartDto(cart, GetLanguageCode());
            }

    public async Task<CartDto> AddToCartAsync(Guid userId, AddToCartRequest request)
    {
        // Get ProductPrice with related Weight data
        var productPrice = await _context.ProductPrices
            .Include(pp => pp.Product)
            .ThenInclude(p => p.Translations)
            .Include(pp => pp.Weight)
            .FirstOrDefaultAsync(pp => pp.Id == request.ProductPriceId) ?? throw new NotFoundException($"ProductPrice with ID {request.ProductPriceId} not found");
        var product = productPrice.Product ?? throw new NotFoundException($"Product for ProductPrice {request.ProductPriceId} not found");

        // Get product name from translation
        var productName = product.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? "Unknown";

        // Calculate total weight needed in KG
        decimal totalWeightKg = request.Quantity * (productPrice.Weight!.Grams / 1000m);

        // Check stock availability
        if (product.StockInKg < totalWeightKg)
        {
            throw new BadRequestException($"Insufficient stock for product {productName}. Available: {product.StockInKg}kg, Requested: {totalWeightKg}kg");
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

                     return MapToCartDto(cart, GetLanguageCode());
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

                return MapToCartDto(cart, GetLanguageCode());
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
                .ThenInclude(p => p.Translations)
                .Include(pp => pp.Weight)
                .FirstOrDefaultAsync(pp => pp.Id == item.ProductPriceId);

            if (productPrice == null)
            {
                throw new NotFoundException($"ProductPrice with ID {item.ProductPriceId} not found");
            }

            var product = productPrice.Product ?? throw new NotFoundException($"Product for ProductPrice {item.ProductPriceId} not found");

            // Get product name from translation
            var productName = product.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name ?? "Unknown";

            // Check stock availability
            decimal totalWeightKg = item.Quantity * (productPrice.Weight!.Grams / 1000m);
            if (product.StockInKg < totalWeightKg)
            {
                throw new BadRequestException($"Insufficient stock for product {productName}. Available: {product.StockInKg}kg, Requested: {totalWeightKg}kg");
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

            return MapToCartDto(cart, GetLanguageCode());
        }

        private CartDto MapToCartDto(Cart cart, string languageCode)
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

                // Get product name from translation with dynamic language support
                var productName = product?.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.Name 
                    ?? product?.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name 
                    ?? "Unknown";

                // Use the specific RoastLevel and GrindType stored in CartItem, not just the first from product
                var roastLevelName = ci.RoastLevel?.Name ?? "Unknown";
                var grindTypeName = ci.GrindType?.Name ?? "Unknown";

                return new CartItemDto(
                    ci.Id,
                    product?.Id ?? Guid.Empty,
                    productPrice?.Id ?? Guid.Empty,
                    productName,
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

