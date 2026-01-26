using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.Interfaces;

/// <summary>
/// Provides currency information from the current request context.
/// Extracts currency from X-Currency header with fallback to USD.
/// </summary>
public interface ICurrencyProvider
{
    /// <summary>
    /// Gets the current currency for the request.
    /// </summary>
    Currency GetCurrentCurrency();

    /// <summary>
    /// Gets the current currency as a string.
    /// </summary>
    string GetCurrentCurrencyCode();

    /// <summary>
    /// Attempts to parse a currency string to Currency enum.
    /// </summary>
    bool TryParseCurrency(string currencyCode, out Currency currency);

    /// <summary>
    /// Gets all supported currencies.
    /// </summary>
    IEnumerable<Currency> GetSupportedCurrencies();
}
