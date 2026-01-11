using DuneFlame.Application.DTOs.Reward;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class RewardService(AppDbContext context) : IRewardService
{
    private readonly AppDbContext _context = context;
    private const decimal CashbackPercentage = 0.05m; // 5% cashback

    public async Task<RewardStatsDto> GetWalletAsync(Guid userId)
    {
        var wallet = await _context.RewardWallets
            .Include(w => w.Transactions)
            .FirstOrDefaultAsync(w => w.UserId == userId);

        if (wallet == null)
        {
            throw new KeyNotFoundException($"Reward wallet not found for user {userId}");
        }

        var totalEarned = wallet.Transactions
            .Where(t => t.Type == RewardType.Earned || t.Type == RewardType.Refunded)
            .Sum(t => t.Amount);

        var totalSpent = wallet.Transactions
            .Where(t => t.Type == RewardType.Redeemed)
            .Sum(t => Math.Abs(t.Amount));

        return new RewardStatsDto(wallet.Balance, totalEarned, totalSpent);
    }

    public async Task<List<RewardTransactionDto>> GetTransactionsAsync(Guid userId)
    {
        var transactions = await _context.RewardTransactions
            .Where(rt => rt.RewardWallet!.UserId == userId)
            .OrderByDescending(rt => rt.CreatedAt)
            .ToListAsync();

        return transactions.Select(t => new RewardTransactionDto(
            t.Id,
            t.CreatedAt,
            t.Amount,
            t.Type,
            t.Description
        )).ToList();
    }

    public async Task EarnPointsAsync(Guid userId, Guid orderId, decimal amount)
    {
        var wallet = await GetOrCreateWalletAsync(userId);

        var transaction = new RewardTransaction
        {
            WalletId = wallet.Id,
            Amount = amount,
            Type = RewardType.Earned,
            Description = $"Cashback for Order #{orderId:N}",
            RelatedOrderId = orderId
        };

        wallet.Transactions.Add(transaction);
        wallet.Balance += amount;

        await _context.SaveChangesAsync();
    }

    public async Task RedeemPointsAsync(Guid userId, decimal amount, Guid orderId)
    {
        var wallet = await _context.RewardWallets
            .Include(w => w.Transactions)
            .FirstOrDefaultAsync(w => w.UserId == userId);

        if (wallet == null)
        {
            throw new KeyNotFoundException($"Reward wallet not found for user {userId}");
        }

        if (wallet.Balance < amount)
        {
            throw new InvalidOperationException(
                $"Insufficient reward balance. Available: {wallet.Balance}, Requested: {amount}");
        }

        var transaction = new RewardTransaction
        {
            WalletId = wallet.Id,
            Amount = -amount, // Negative for redemption
            Type = RewardType.Redeemed,
            Description = $"Points redeemed for Order #{orderId:N}",
            RelatedOrderId = orderId
        };

        wallet.Transactions.Add(transaction);
        wallet.Balance -= amount;

        await _context.SaveChangesAsync();
    }

    public async Task RefundPointsAsync(Guid userId, Guid orderId)
    {
        var wallet = await _context.RewardWallets
            .Include(w => w.Transactions)
            .FirstOrDefaultAsync(w => w.UserId == userId);

        if (wallet == null)
        {
            throw new KeyNotFoundException($"Reward wallet not found for user {userId}");
        }

        // Find related transactions
        var earnedTransaction = wallet.Transactions
            .FirstOrDefault(t => t.RelatedOrderId == orderId && t.Type == RewardType.Earned);

        var redeemedTransaction = wallet.Transactions
            .FirstOrDefault(t => t.RelatedOrderId == orderId && t.Type == RewardType.Redeemed);

        if (earnedTransaction != null)
        {
            // Reverse earned points
            var refundTransaction = new RewardTransaction
            {
                WalletId = wallet.Id,
                Amount = -earnedTransaction.Amount,
                Type = RewardType.Refunded,
                Description = $"Refund for Order #{orderId:N}",
                RelatedOrderId = orderId
            };

            wallet.Transactions.Add(refundTransaction);
            wallet.Balance -= earnedTransaction.Amount;
        }

        if (redeemedTransaction != null)
        {
            // Reverse redeemed points
            var refundTransaction = new RewardTransaction
            {
                WalletId = wallet.Id,
                Amount = Math.Abs(redeemedTransaction.Amount),
                Type = RewardType.Refunded,
                Description = $"Refund for Order #{orderId:N}",
                RelatedOrderId = orderId
            };

            wallet.Transactions.Add(refundTransaction);
            wallet.Balance += Math.Abs(redeemedTransaction.Amount);
        }

        await _context.SaveChangesAsync();
    }

    public async Task AdminAdjustPointsAsync(Guid userId, decimal amount, string reason)
    {
        var wallet = await GetOrCreateWalletAsync(userId);

        var transaction = new RewardTransaction
        {
            WalletId = wallet.Id,
            Amount = amount,
            Type = RewardType.ManualAdjustment,
            Description = reason,
            RelatedOrderId = null
        };

        wallet.Transactions.Add(transaction);
        wallet.Balance += amount;

        await _context.SaveChangesAsync();
    }

    private async Task<RewardWallet> GetOrCreateWalletAsync(Guid userId)
    {
        var wallet = await _context.RewardWallets
            .Include(w => w.Transactions)
            .FirstOrDefaultAsync(w => w.UserId == userId);

        if (wallet != null)
        {
            return wallet;
        }

        wallet = new RewardWallet
        {
            UserId = userId,
            Balance = 0
        };

        _context.RewardWallets.Add(wallet);
        await _context.SaveChangesAsync();

        return wallet;
    }

    public static decimal CalculateCashback(decimal orderTotal)
    {
        return orderTotal * CashbackPercentage;
    }
}
