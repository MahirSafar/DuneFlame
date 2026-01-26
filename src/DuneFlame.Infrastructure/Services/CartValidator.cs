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
    public void ValidateAddToCart(Cart cart, ProductPrice productPrice)
    {
        if (productPrice == null)
            throw new ArgumentNullException(nameof(productPrice));

        // If cart is empty, set its currency
        if (cart.Items.Count == 0)
        {
            cart.SetCurrency(productPrice.CurrencyCode);
            return;
        }

        // If cart has items, validate currency consistency
        cart.ValidateCurrencyConsistency(productPrice.CurrencyCode);
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

        var firstCurrency = cart.Items.First().ProductPrice?.CurrencyCode;
        return firstCurrency == null || cart.Items.All(item => item.ProductPrice?.CurrencyCode == firstCurrency);
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
