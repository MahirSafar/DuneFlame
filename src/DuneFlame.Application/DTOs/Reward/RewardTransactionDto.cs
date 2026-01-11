using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Reward;

public record RewardTransactionDto(
    Guid Id,
    DateTime CreatedAt,
    decimal Amount,
    RewardType Type,
    string Description
);
