using DuneFlame.Application.DTOs.Reward;
using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

public interface IRewardService
{
    Task<RewardStatsDto> GetWalletAsync(Guid userId);
    Task<List<RewardTransactionDto>> GetTransactionsAsync(Guid userId);
    Task EarnPointsAsync(Guid userId, Guid orderId, decimal amount);
    Task RedeemPointsAsync(Guid userId, decimal amount, Guid orderId);
    // Unit of Work pattern: Accepts Order object, modifies entities but does NOT save
    Task RefundPointsAsync(Order order);
    Task AdminAdjustPointsAsync(Guid userId, decimal amount, string reason);
}
