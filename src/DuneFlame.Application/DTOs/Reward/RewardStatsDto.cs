namespace DuneFlame.Application.DTOs.Reward;

public record RewardStatsDto(
    decimal Balance,
    decimal TotalEarned,
    decimal TotalSpent
);
