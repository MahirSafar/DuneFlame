namespace DuneFlame.Application.DTOs.Order;

/// <summary>
/// Response after creating an order.
/// Includes PaymentIntent details needed for the frontend to display Stripe Payment Element.
/// </summary>
public record CreateOrderResponse(
    Guid OrderId,
    decimal TotalAmount,
    string Currency,
    string ClientSecret,
    string PaymentIntentId,
    string ShippingAddress
);
