namespace DuneFlame.Application.DTOs.Order;

public record CreateOrderRequest(
    string BasketId,
    AddressDto ShippingAddress,
    string? PaymentIntentId = null,
    bool UsePoints = false
);
