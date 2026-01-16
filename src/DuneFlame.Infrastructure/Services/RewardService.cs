using DuneFlame.Application.DTOs.Reward;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Domain.Exceptions;
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
            throw new NotFoundException($"Reward wallet not found for user {userId}");
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
            throw new NotFoundException($"Reward wallet not found for user {userId}");
        }

        if (wallet.Balance < amount)
        {
            throw new BadRequestException(
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

    public async Task RefundPointsAsync(Order order)
    {
        // NOTE: This method participates in Unit of Work pattern.
        // It modifies entities but does NOT call SaveChangesAsync().
        // The caller (AdminOrderService) handles the final save.
        
        if (order == null)
        {
            throw new ArgumentNullException(nameof(order), "Order cannot be null");
        }

        // Step 1: Get or create user's reward wallet
        var wallet = await GetOrCreateWalletAsync(order.UserId);

        // Step 2: Handle Points Earned (The Takeback)
        if (order.PointsEarned > 0)
        {
            // SUBTRACT earned points from balance (allow negative balance - user debt)
            wallet.Balance -= order.PointsEarned;

            var earnedReversal = new RewardTransaction
            {
                WalletId = wallet.Id,
                Amount = -order.PointsEarned,
                Type = RewardType.Refunded,
                Description = $"Points reversed due to cancellation of Order #{order.Id:N}",
                RelatedOrderId = order.Id
            };

            wallet.Transactions.Add(earnedReversal);
        }

        // Step 3: Handle Points Redeemed (The Refund)
        if (order.PointsRedeemed > 0)
        {
            // ADD redeemed points back to balance
            wallet.Balance += order.PointsRedeemed;

            var redeemedRefund = new RewardTransaction
            {
                WalletId = wallet.Id,
                Amount = order.PointsRedeemed,
                Type = RewardType.Refunded,
                Description = $"Points returned from cancellation of Order #{order.Id:N}",
                RelatedOrderId = order.Id
            };

            wallet.Transactions.Add(redeemedRefund);
        }

        // NOTE: Do NOT save here. Changes remain in memory, tracked by EF Core.
        // The caller's SaveChangesAsync() will persist all changes atomically.
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
        // Check Local context first to prevent tracking conflicts
        // (wallet may have been added in current session but not yet saved)
        var wallet = _context.RewardWallets.Local.FirstOrDefault(w => w.UserId == userId);

        if (wallet != null)
        {
            return wallet;
        }

        // Not in local context, query database
        wallet = await _context.RewardWallets
            .Include(w => w.Transactions)
            .FirstOrDefaultAsync(w => w.UserId == userId);

        if (wallet != null)
        {
            return wallet;
        }

        // Create new wallet but DON'T save yet
        // EF Core will insert it when caller calls SaveChangesAsync
        wallet = new RewardWallet
        {
            UserId = userId,
            Balance = 0
        };

        _context.RewardWallets.Add(wallet);
        // NOTE: Do NOT call SaveChangesAsync here
        // The caller (RefundPointsAsync or AdminAdjustPointsAsync) will save atomically

        return wallet;
    }

    public static decimal CalculateCashback(decimal orderTotal)
    {
        return orderTotal * CashbackPercentage;
    }
}
