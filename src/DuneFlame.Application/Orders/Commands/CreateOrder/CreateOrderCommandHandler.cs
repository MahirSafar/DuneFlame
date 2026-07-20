using DuneFlame.Application.DTOs.Order;
using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Orders.Commands.CreateOrder;

public class CreateOrderCommandHandler(IOrderService orderService)
    : IRequestHandler<CreateOrderCommand, OrderDto>
{
    public Task<OrderDto> Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var request = new CreateOrderRequest(
            BasketId: command.BasketId,
            ShippingAddress: command.ShippingAddress,
            Currency: command.Currency,
            PaymentIntentId: command.PaymentIntentId,
            UsePoints: command.UsePoints,
            LanguageCode: command.LanguageCode,
            Channel: command.Channel,
            ShippingMethodName: command.ShippingMethodName,
            PaymentMethod: command.PaymentMethod
        );

        return orderService.CreateOrderAsync(command.UserId, request);
    }
}
