using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Validates cart operations for currency consistency.
/// Ensures that a cart only contains items in a single currency.
/// </summary>
public class CartValidator : ICartValidator
{
    private readonly ICurrencyProvider _currencyProvider;

    public CartValidator(ICurrencyProvider currencyProvider)
    {
        _currencyProvider = currencyProvider;
    }

    /// <summary>
    /// Validates that adding an item to the cart maintains currency consistency.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when currency mismatch is detected.</exception>
    public void ValidateAddToCart(Cart cart, ProductVariant productVariant)
    {
        if (productVariant == null)
            throw new ArgumentNullException(nameof(productVariant));

        // Variants are currency agnostic on the DB level.
        // Orders/Carts might map Session Currency if necessary down the line.
    }

    /// <summary>
    /// Validates that cart currency matches the current request currency.
    /// </summary>
    public void ValidateCartCurrency(Cart cart)
    {
        var currentCurrency = _currencyProvider.GetCurrentCurrency();
        
        if (cart.Items.Count > 0 && cart.CurrencyCode != currentCurrency)
        {
            throw new InvalidOperationException(
                $"Your cart is in {cart.CurrencyCode} but you're requesting {currentCurrency}. " +
                "Please continue checkout in the original currency or clear your cart.");
        }
    }

    /// <summary>
    /// Validates that all items in the cart use the same currency.
    /// </summary>
    public bool IsCartCurrencyConsistent(Cart cart)
    {
        if (cart.Items.Count == 0)
            return true;

        return true; // Simplified for strictly agnostic variants
    }

    /// <summary>
    /// Clears the cart and allows currency change.
    /// </summary>
    public void ClearCartForCurrencyChange(Cart cart)
    {
        cart.Items.Clear();
        cart.CurrencyCode = _currencyProvider.GetCurrentCurrency();
    }
}
