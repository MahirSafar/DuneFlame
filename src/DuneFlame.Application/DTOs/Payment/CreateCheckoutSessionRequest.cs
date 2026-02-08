namespace DuneFlame.Application.DTOs.Payment;

public record CreateCheckoutSessionRequest(
    string ItemCode,
    decimal Quantity,
    string SuccessUrl,
    string CancelUrl
);
