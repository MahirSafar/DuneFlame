using DuneFlame.Application.DTOs.Order;
using DuneFlame.Domain.Enums;
using MediatR;

namespace DuneFlame.Application.Orders.Commands.CreateOrder;

public record CreateOrderCommand(
    Guid? UserId,
    string BasketId,
    AddressDto ShippingAddress,
    string Currency,
    string? PaymentIntentId = null,
    bool UsePoints = false,
    string LanguageCode = "en",
    OrderChannel Channel = OrderChannel.Web,
    string? ShippingMethodName = null,
    PaymentMethod PaymentMethod = PaymentMethod.Online
) : IRequest<OrderDto>;
