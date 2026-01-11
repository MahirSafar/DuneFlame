using DuneFlame.Application.DTOs.Reward;

namespace DuneFlame.Application.Interfaces;

public interface IRewardService
{
    Task<RewardStatsDto> GetWalletAsync(Guid userId);
    Task<List<RewardTransactionDto>> GetTransactionsAsync(Guid userId);
    Task EarnPointsAsync(Guid userId, Guid orderId, decimal amount);
    Task RedeemPointsAsync(Guid userId, decimal amount, Guid orderId);
    Task RefundPointsAsync(Guid userId, Guid orderId);
    Task AdminAdjustPointsAsync(Guid userId, decimal amount, string reason);
}
