namespace DuneFlame.Application.Interfaces;

public interface IPaymentService
{
    Task<PaymentIntentResponse> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId);
}

public record PaymentIntentResponse(
    string ClientSecret,
    string PaymentIntentId
);
