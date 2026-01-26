namespace DuneFlame.Application.Interfaces;

public interface IPaymentService
{
    Task<PaymentIntentResponse> CreatePaymentIntentAsync(decimal amount, string currency, Guid orderId, string? basketId = null);
    Task<PaymentIntentResponse> GetPaymentIntentAsync(string paymentIntentId);
    Task<RefundResponse> RefundPaymentAsync(string transactionId, decimal amount);
    Task<PaymentIntentResponse> CreateOrUpdatePaymentIntentAsync(string basketId, decimal amount, string currency = "usd");
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
