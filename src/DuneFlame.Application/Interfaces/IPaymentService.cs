namespace DuneFlame.Application.Interfaces;

public interface IPaymentService
{
    Task<PaymentIntentResponse> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId);
    Task<RefundResponse> RefundPaymentAsync(string transactionId, decimal amount);
}

public record PaymentIntentResponse(
    string ClientSecret,
    string PaymentIntentId
);

public record RefundResponse(
    string RefundId,
    decimal Amount,
    string Status
);
