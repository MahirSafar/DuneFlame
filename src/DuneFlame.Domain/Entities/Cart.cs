using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class Cart : BaseEntity
{
    public Guid UserId { get; set; }
    public Currency CurrencyCode { get; set; } = Currency.USD;
    public ApplicationUser? ApplicationUser { get; set; }
    public ICollection<CartItem> Items { get; set; } = [];

    /// <summary>
    /// Validates that a new item's currency matches the cart's currency.
    /// Throws InvalidOperationException if currencies don't match.
    /// </summary>
    public void ValidateCurrencyConsistency(Currency itemCurrency)
    {
        if (Items.Any() && itemCurrency != CurrencyCode)
        {
            throw new InvalidOperationException(
                $"Cannot add item in {itemCurrency} currency to cart with {CurrencyCode} currency. " +
                "Please clear your cart before changing currency.");
        }
    }

    /// <summary>
    /// Sets the cart's currency. Can only be set once or when cart is empty.
    /// </summary>
    public void SetCurrency(Currency currency)
    {
        if (Items.Any() && currency != CurrencyCode)
        {
            throw new InvalidOperationException(
                $"Cannot change cart currency from {CurrencyCode} to {currency}. " +
                "Please clear your cart before changing currency.");
        }

        CurrencyCode = currency;
    }
}
