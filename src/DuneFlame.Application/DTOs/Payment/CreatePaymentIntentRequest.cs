namespace DuneFlame.Application.DTOs.Payment;

public record CreatePaymentIntentRequest(
    Guid OrderId
);
