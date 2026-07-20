using MediatR;

namespace DuneFlame.Application.Orders.Commands.CancelOrder;

public record CancelOrderCommand(Guid OrderId) : IRequest;
