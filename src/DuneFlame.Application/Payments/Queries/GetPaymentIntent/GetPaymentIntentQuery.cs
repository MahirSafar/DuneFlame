using DuneFlame.Application.Interfaces;
using MediatR;

namespace DuneFlame.Application.Payments.Queries.GetPaymentIntent;

public record GetPaymentIntentQuery(string PaymentIntentId) : IRequest<PaymentIntentResponse>;

public class GetPaymentIntentQueryHandler(IPaymentService paymentService)
    : IRequestHandler<GetPaymentIntentQuery, PaymentIntentResponse>
{
    public Task<PaymentIntentResponse> Handle(GetPaymentIntentQuery query, CancellationToken cancellationToken)
        => paymentService.GetPaymentIntentAsync(query.PaymentIntentId);
}
