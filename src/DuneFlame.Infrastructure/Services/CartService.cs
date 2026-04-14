using DuneFlame.Application.DTOs.Cart;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class CartService(AppDbContext context, IHttpContextAccessor httpContextAccessor, ICurrencyProvider currencyProvider) : ICartService
{
    private readonly AppDbContext _context = context;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ICurrencyProvider _currencyProvider = currencyProvider;

    private IQueryable<Cart> GetCartQuery()
    {
        return _context.Carts
            .Include(c => c.Items)
                .ThenInclude(ci => ci.ProductVariant)
                .ThenInclude(v => v.Product)
                .ThenInclude(p => p.Images)
            .Include(c => c.Items)
                .ThenInclude(ci => ci.ProductVariant)
                .ThenInclude(v => v.Product)
                .ThenInclude(p => p.Translations)
            .Include(c => c.Items)
                .ThenInclude(ci => ci.ProductVariant)
                .ThenInclude(v => v.Prices)
            .Include(c => c.Items)
                .ThenInclude(ci => ci.ProductVariant)
                .ThenInclude(v => v.Options)
                .ThenInclude(o => o.ProductAttributeValue)
                .ThenInclude(av => av.ProductAttribute)
            .Include(c => c.Items)
                .ThenInclude(ci => ci.RoastLevel)
            .Include(c => c.Items)
                .ThenInclude(ci => ci.GrindType)
            .AsSplitQuery();
    }

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
        var cart = await GetCartQuery()
            .FirstOrDefaultAsync(c => c.UserId == userId) ?? throw new NotFoundException($"Cart not found for user {userId}");
        return MapToCartDto(cart, GetLanguageCode());
    }

    public async Task<CartDto> AddToCartAsync(Guid userId, AddToCartRequest request)
    {
        // Get ProductVariant with relationships
        var productVariant = await _context.ProductVariants
            .Include(v => v.Product)
            .ThenInclude(p => p.Translations)
            .Include(v => v.Options)
            .ThenInclude(o => o.ProductAttributeValue)
            .ThenInclude(av => av.ProductAttribute)
            .FirstOrDefaultAsync(v => v.Id == request.ProductVariantId) ?? throw new NotFoundException($"ProductVariant with ID {request.ProductVariantId} not found");
        var product = productVariant.Product ?? throw new NotFoundException($"Product for ProductVariant {request.ProductVariantId} not found");

        // Get product name from translation
        var productName = product.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name 
            ?? product.Slug 
            ?? "Unknown";

        // Check stock availability
        if (productVariant.StockQuantity.HasValue && productVariant.StockQuantity.Value < request.Quantity)
        {
            throw new BadRequestException($"Insufficient stock for product {productName}. Available: {productVariant.StockQuantity.Value}, Requested: {request.Quantity}");
        }

        var cart = await _context.Carts
            .Include(c => c.Items)
            .ThenInclude(ci => ci.ProductVariant)
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
            ci.ProductVariantId == request.ProductVariantId && 
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
                ProductVariantId = request.ProductVariantId,
                RoastLevelId = roastLevelId,
                GrindTypeId = grindTypeId,
                Quantity = request.Quantity
            };
            cart.Items.Add(cartItem);
        }

            await _context.SaveChangesAsync();

             cart = await GetCartQuery()
                 .FirstAsync(c => c.Id == cart.Id);

             return MapToCartDto(cart, GetLanguageCode());
                 }

    public async Task<CartDto> RemoveFromCartAsync(Guid userId, Guid itemId)
    {
        var cart = await GetCartQuery()
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
    /// Bulk sync cart items from frontend. Groups items by ProductVariantId + RoastLevelId + GrindTypeId
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
                item.ProductVariantId,
                item.RoastLevelId == Guid.Empty ? Guid.Empty : item.RoastLevelId,
                item.GrindTypeId == Guid.Empty ? Guid.Empty : item.GrindTypeId,
                item.Quantity
            ))
            .ToList();

        // Group items by variation key (ProductVariantId + RoastLevelId + GrindTypeId)
        // Normalizing Guid.Empty ensures consistent grouping across requests
        // Sum quantities for duplicates
        var groupedItems = normalizedItems
            .GroupBy(item => new
            {
                item.ProductVariantId,
                item.RoastLevelId,
                item.GrindTypeId
            })
            .Select(group => new AddToCartRequest(
                group.Key.ProductVariantId,
                group.Key.RoastLevelId,
                group.Key.GrindTypeId,
                group.Sum(item => item.Quantity)
            ))
            .ToList();

        // Validate and create CartItems
        foreach (var item in groupedItems)
        {
            // Verify ProductVariant exists
            var productVariant = await _context.ProductVariants
                .Include(v => v.Product)
                .ThenInclude(p => p.Translations)
                .Include(v => v.Options)
                .ThenInclude(o => o.ProductAttributeValue)
                .ThenInclude(av => av.ProductAttribute)
                .FirstOrDefaultAsync(v => v.Id == item.ProductVariantId);

            if (productVariant == null)
            {
                throw new NotFoundException($"ProductVariant with ID {item.ProductVariantId} not found");
            }

            var product = productVariant.Product ?? throw new NotFoundException($"Product for ProductVariant {item.ProductVariantId} not found");

            // Get product name from translation
            var productName = product.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name 
                ?? product.Slug 
                ?? "Unknown";

            // Check stock availability
            if (productVariant.StockQuantity.HasValue && productVariant.StockQuantity.Value < item.Quantity)
            {
                throw new BadRequestException($"Insufficient stock for variant {productName}. Available: {productVariant.StockQuantity.Value}, Requested: {item.Quantity}");
            }

            // Create CartItem with deduplicated quantity
            var cartItem = new CartItem
            {
                CartId = cart.Id,
                ProductVariantId = item.ProductVariantId,
                RoastLevelId = item.RoastLevelId,
                GrindTypeId = item.GrindTypeId,
                Quantity = item.Quantity
            };

            cart.Items.Add(cartItem);
        }

        await _context.SaveChangesAsync();

        // Reload cart with all relationships for response
        cart = await GetCartQuery()
            .FirstAsync(c => c.Id == cart.Id);

        return MapToCartDto(cart, GetLanguageCode());
    }

            private CartDto MapToCartDto(Cart cart, string languageCode)
            {
                var currentCurrency = _currencyProvider.GetCurrentCurrency();

                var totalAmount = cart.Items.Sum(ci =>
                {
                    var variant = ci.ProductVariant;
                    if (variant == null) return 0;
                    var resolvedPrice = variant.Prices.FirstOrDefault(p => p.Currency == currentCurrency)?.Price ?? variant.Price;
                    return resolvedPrice * ci.Quantity;
                });

                var cartItems = cart.Items.Select(ci => 
                {
                    var variant = ci.ProductVariant;
                    var product = variant?.Product;
                    var resolvedPrice = variant?.Prices.FirstOrDefault(p => p.Currency == currentCurrency)?.Price ?? variant?.Price ?? 0;

                    // Get product name from translation with dynamic language support
                    var productName = product?.Translations?.FirstOrDefault(t => t.LanguageCode == languageCode)?.Name 
                        ?? product?.Translations?.FirstOrDefault(t => t.LanguageCode == "en")?.Name 
                        ?? product?.Translations?.FirstOrDefault()?.Name 
                        ?? product?.Slug
                        ?? "Unknown";

                    // Use the specific RoastLevel and GrindType stored in CartItem, not just the first from product
                    var roastLevelName = ci.RoastLevel?.Name ?? "Unknown";
                    var grindTypeName = ci.GrindType?.Name ?? "Unknown";

                    var attributes = variant?.Options
                        .Where(o => o.ProductAttributeValue?.ProductAttribute != null)
                        .Select(o => $"{o.ProductAttributeValue!.ProductAttribute!.Name}: {o.ProductAttributeValue.Value}")
                        .ToList() ?? new List<string>();

                    return new CartItemDto(
                        ci.Id,
                        product?.Id ?? Guid.Empty,
                        variant?.Id ?? Guid.Empty,
                        productName,
                        resolvedPrice,
                        ci.Quantity,
                        product?.Images.FirstOrDefault()?.ImageUrl,
                        variant?.Sku ?? string.Empty,
                        attributes,
                        roastLevelName,
                        grindTypeName,
                        ci.RoastLevelId,
                        ci.GrindTypeId
                    );
                }).ToList();

                return new CartDto(cart.Id, totalAmount, cartItems);
            }
        }

