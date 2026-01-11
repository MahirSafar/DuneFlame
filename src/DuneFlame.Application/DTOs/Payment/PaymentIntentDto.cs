namespace DuneFlame.Application.DTOs.Payment;

public record PaymentIntentDto(
    string ClientSecret,
    string PaymentIntentId,
    decimal Amount
);
