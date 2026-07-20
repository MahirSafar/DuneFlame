using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Order;

/// <summary>
/// Request to create an order from the basket with currency-aware pricing.
/// Currency MUST match the basket currency and all item prices will be validated against the database.
/// </summary>
public record CreateOrderRequest(
    string BasketId,
    AddressDto ShippingAddress,
    string Currency,
    string? PaymentIntentId = null,
    bool UsePoints = false,
    string LanguageCode = "en",
    OrderChannel Channel = OrderChannel.Web,
    string? ShippingMethodName = null,
    PaymentMethod PaymentMethod = PaymentMethod.Online
);
