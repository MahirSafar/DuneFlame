namespace DuneFlame.Application.DTOs.Order;

public record CreateOrderRequest(
    string ShippingAddress,
    bool UsePoints = false
);
