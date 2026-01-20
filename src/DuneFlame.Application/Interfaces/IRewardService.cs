using DuneFlame.Application.DTOs.Reward;

namespace DuneFlame.Application.Interfaces;

public interface IRewardService
{
    Task<RewardStatsDto> GetWalletAsync(Guid userId);
    Task<List<RewardTransactionDto>> GetTransactionsAsync(Guid userId);
    Task EarnPointsAsync(Guid userId, Guid orderId, decimal amount);
    Task RedeemPointsAsync(Guid userId, decimal amount, Guid orderId);
    // Unit of Work pattern: Accept only primitives to prevent entity re-attachment
    // Does NOT call SaveChangesAsync - caller is responsible for final save
    Task RefundPointsAsync(Guid userId, Guid orderId, decimal pointsEarned, decimal pointsRedeemed);
    Task AdminAdjustPointsAsync(Guid userId, decimal amount, string reason);
}
