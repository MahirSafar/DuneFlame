using DuneFlame.Application.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Middleware;

/// <summary>
/// Middleware that validates the X-Currency header against supported currencies.
/// Returns 400 Bad Request if an invalid currency is provided.
/// </summary>
public class CurrencyValidationMiddleware
{
    private readonly RequestDelegate _next;
    private const string CurrencyHeaderKey = "X-Currency";

    public CurrencyValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrencyProvider currencyProvider)
    {
        // Check if X-Currency header is present and validate it
        if (context.Request.Headers.TryGetValue(CurrencyHeaderKey, out var currencyHeader))
        {
            var currencyValue = currencyHeader.ToString().Trim();

            // If header is provided, it must be valid
            if (!string.IsNullOrWhiteSpace(currencyValue) && !currencyProvider.TryParseCurrency(currencyValue, out _))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";

                var errorResponse = new
                {
                    message = "Invalid currency code provided in X-Currency header.",
                    supportedCurrencies = currencyProvider.GetSupportedCurrencies()
                        .Select(c => c.ToString())
                        .OrderBy(c => c)
                        .ToList()
                };

                await context.Response.WriteAsJsonAsync(errorResponse);
                return;
            }
        }

        await _next(context);
    }
}

/// <summary>
/// Extension methods for registering currency validation middleware.
/// </summary>
public static class CurrencyValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseCurrencyValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CurrencyValidationMiddleware>();
    }
}
