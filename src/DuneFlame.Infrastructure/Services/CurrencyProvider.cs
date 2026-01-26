using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Implementation of ICurrencyProvider that extracts currency from request context.
/// </summary>
public class CurrencyProvider : ICurrencyProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string CurrencyHeaderKey = "X-Currency";
    private static readonly Currency DefaultCurrency = Currency.USD;

    public CurrencyProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    /// <summary>
    /// Extracts currency from X-Currency header, falls back to USD if not provided or invalid.
    /// </summary>
    public Currency GetCurrentCurrency()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return DefaultCurrency;

        if (httpContext.Request.Headers.TryGetValue(CurrencyHeaderKey, out var currencyHeader))
        {
            var currencyValue = currencyHeader.ToString().Trim();
            if (TryParseCurrency(currencyValue, out var currency))
                return currency;
        }

        return DefaultCurrency;
    }

    public string GetCurrentCurrencyCode()
    {
        return GetCurrentCurrency().ToString();
    }

    public bool TryParseCurrency(string currencyCode, out Currency currency)
    {
        currency = Currency.USD;
        
        if (string.IsNullOrWhiteSpace(currencyCode))
            return false;

        return Enum.TryParse<Currency>(currencyCode.Trim(), ignoreCase: true, out currency);
    }

    public IEnumerable<Currency> GetSupportedCurrencies()
    {
        return Enum.GetValues(typeof(Currency)).Cast<Currency>();
    }
}
