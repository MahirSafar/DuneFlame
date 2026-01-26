using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

/// <summary>
/// Validates cart operations for currency consistency.
/// </summary>
public interface ICartValidator
{
    /// <summary>
    /// Validates that adding an item to the cart maintains currency consistency.
    /// </summary>
    void ValidateAddToCart(Cart cart, ProductPrice productPrice);

    /// <summary>
    /// Validates that cart currency matches the current request currency.
    /// </summary>
    void ValidateCartCurrency(Cart cart);

    /// <summary>
    /// Checks if all items in the cart use the same currency.
    /// </summary>
    bool IsCartCurrencyConsistent(Cart cart);

    /// <summary>
    /// Clears the cart and updates currency to current request.
    /// </summary>
    void ClearCartForCurrencyChange(Cart cart);
}
