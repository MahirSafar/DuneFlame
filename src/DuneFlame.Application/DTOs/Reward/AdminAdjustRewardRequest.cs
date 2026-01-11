namespace DuneFlame.Application.DTOs.Reward;

public record AdminAdjustRewardRequest(
    Guid UserId,
    decimal Amount,
    string Reason
);
