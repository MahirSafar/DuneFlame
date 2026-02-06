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

        // Auto-create wallet if it doesn't exist
        if (wallet == null)
        {
            wallet = new RewardWallet
            {
                UserId = userId,
                Balance = 0
            };

            _context.RewardWallets.Add(wallet);
            await _context.SaveChangesAsync();
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
            .AsNoTracking()
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

    /// <summary>
    /// Earn points for a completed order with idempotency check.
    /// PHASE 1 (ATOMIC): Check idempotency, fetch wallet
    /// PHASE 2 (SHADOW): Apply balance update via atomic SQL
    /// PHASE 3 (COMMIT): Record transaction entry
    /// </summary>
    public async Task EarnPointsAsync(Guid userId, Guid orderId, decimal amount)
    {
        // PHASE 1: Idempotency Check (prevent duplicate earnings)
        var existingTransaction = await _context.RewardTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.RelatedOrderId == orderId && t.Type == RewardType.Earned);

        if (existingTransaction != null)
        {
            // Already processed, skip silently (idempotent)
            return;
        }

        // Get wallet (fetch fresh to avoid stale data)
        var wallet = await GetOrCreateWalletAsync(userId);

        // PHASE 2: Atomic Balance Update (shadow update via native SQL)
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE ""RewardWallets"" SET ""Balance"" = ""Balance"" + {amount} 
               WHERE ""Id"" = {wallet.Id}");

        // PHASE 3: Record transaction entry
        var transaction = new RewardTransaction
        {
            WalletId = wallet.Id,
            Amount = amount,
            Type = RewardType.Earned,
            Description = $"Cashback for Order #{orderId:N}",
            RelatedOrderId = orderId
        };

        _context.RewardTransactions.Add(transaction);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Redeem points for an order with balance validation.
    /// PHASE 1 (ATOMIC): Validate wallet and balance
    /// PHASE 2 (SHADOW): Apply balance update via atomic SQL
    /// PHASE 3 (COMMIT): Record transaction entry
    /// </summary>
    public async Task RedeemPointsAsync(Guid userId, decimal amount, Guid orderId)
    {
        // PHASE 1: Fetch wallet for validation (read-only, no tracking)
        var wallet = await _context.RewardWallets
            .AsNoTracking()
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

        // PHASE 2: Atomic Balance Update (shadow update via native SQL)
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE ""RewardWallets"" SET ""Balance"" = ""Balance"" - {amount}, ""UpdatedAt"" = {DateTime.UtcNow} 
               WHERE ""Id"" = {wallet.Id} AND ""Balance"" >= {amount}");

        // PHASE 3: Record transaction entry
        var transaction = new RewardTransaction
        {
            WalletId = wallet.Id,
            Amount = -amount,
            Type = RewardType.Redeemed,
            Description = $"Points redeemed for Order #{orderId:N}",
            RelatedOrderId = orderId
        };

        _context.RewardTransactions.Add(transaction);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Refund points due to order cancellation.
    /// Part of Unit of Work pattern - does NOT call SaveChangesAsync.
    /// CRITICAL FIX: Uses Native SQL for balance update to avoid MVCC concurrency conflicts.
    /// - Phase 1: Fetch wallet (read-only, no tracking)
    /// - Phase 2: Update balance via native SQL (bypasses EF Core concurrency checks)
    /// - Phase 3: Record transactions via EF Core INSERTS (no concurrency risk on new rows)
    /// </summary>
    public async Task RefundPointsAsync(Guid userId, Guid orderId, decimal pointsEarned, decimal pointsRedeemed)
    {
        // PHASE 1: Get or Create Wallet (read-only, no tracking)
        var wallet = await _context.RewardWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId);

        if (wallet == null)
        {
            // Edge case: Create new wallet with adjusted balance
            // (rare in cancellation flow, but defensive)
            decimal newBalance = -pointsEarned + pointsRedeemed;
            wallet = new RewardWallet
            {
                UserId = userId,
                Balance = newBalance
            };
            _context.RewardWallets.Add(wallet);
        }
        else
        {
            // PHASE 2: Native SQL for Balance Update (bypasses EF Core concurrency checks)
            // Calculate net adjustment: earned points decrease balance, redeemed points increase it
            decimal balanceAdjustment = -pointsEarned + pointsRedeemed;

            if (balanceAdjustment != 0)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE ""RewardWallets"" 
                       SET ""Balance"" = ""Balance"" + {balanceAdjustment}, 
                           ""UpdatedAt"" = {DateTime.UtcNow}
                       WHERE ""Id"" = {wallet.Id}");
            }
        }

        // PHASE 3: Record Transaction Entries via EF Core INSERTS (no concurrency risk)
        // Add transactions directly to context, NOT through wallet.Transactions to keep wallet detached
        if (pointsEarned > 0)
        {
            var earnedReversal = new RewardTransaction
            {
                WalletId = wallet.Id,
                Amount = -pointsEarned,
                Type = RewardType.Refunded,
                Description = $"Points reversed due to cancellation of Order #{orderId:N}",
                RelatedOrderId = orderId
            };

            _context.RewardTransactions.Add(earnedReversal);
        }

        if (pointsRedeemed > 0)
        {
            var redeemedRefund = new RewardTransaction
            {
                WalletId = wallet.Id,
                Amount = pointsRedeemed,
                Type = RewardType.Refunded,
                Description = $"Points returned from cancellation of Order #{orderId:N}",
                RelatedOrderId = orderId
            };

            _context.RewardTransactions.Add(redeemedRefund);
        }
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

    /// <summary>
    /// Two-query approach for wallet creation:
    /// 1. Check local context (AsNoTracking)
    /// 2. Query database fresh
    /// 3. Create if not exists (don't save immediately)
    /// </summary>
    private async Task<RewardWallet> GetOrCreateWalletAsync(Guid userId)
    {
        // Query without tracking to check if wallet exists
        var wallet = await _context.RewardWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId);

        if (wallet != null)
        {
            // Re-attach to context for modifications
            return await _context.RewardWallets
                .Include(w => w.Transactions)
                .FirstOrDefaultAsync(w => w.UserId == userId) ?? wallet;
        }

        // Create new wallet but DON'T save yet
        wallet = new RewardWallet
        {
            UserId = userId,
            Balance = 0
        };

        _context.RewardWallets.Add(wallet);

        return wallet;
    }

    public static decimal CalculateCashback(decimal orderTotal)
    {
        return orderTotal * CashbackPercentage;
    }
}
