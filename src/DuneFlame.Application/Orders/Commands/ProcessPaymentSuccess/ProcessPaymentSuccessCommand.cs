using MediatR;

namespace DuneFlame.Application.Orders.Commands.ProcessPaymentSuccess;

public record ProcessPaymentSuccessCommand(string TransactionId) : IRequest;
