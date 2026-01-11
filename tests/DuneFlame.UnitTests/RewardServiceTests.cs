using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using DuneFlame.Infrastructure.Persistence;
using DuneFlame.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;

namespace DuneFlame.UnitTests;

public class RewardServiceTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private AppDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void CalculateCashback_WithOrderTotal_ReturnsCorrectPercentage()
    {
        // Arrange
        decimal orderTotal = 100m;
        decimal expectedCashback = 5m; // 5%

        // Act
        var result = RewardService.CalculateCashback(orderTotal);

        // Assert
        result.Should().Be(expectedCashback);
    }

    [Theory]
    [InlineData(50, 2.5)]
    [InlineData(100, 5)]
    [InlineData(1000, 50)]
    [InlineData(0, 0)]
    public void CalculateCashback_WithVariousAmounts_ReturnsCorrect5Percent(decimal amount, decimal expected)
    {
        // Act
        var result = RewardService.CalculateCashback(amount);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task RedeemPointsAsync_WithInsufficientBalance_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = GetInMemoryDbContext();
        var loggerMock = new Mock<ILogger<RewardService>>();
        var service = new RewardService(context);

        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var balance = 30m;
        var redeemAmount = 50m;

        var wallet = new RewardWallet
        {
            UserId = userId,
            Balance = balance,
            Transactions = []
        };

        context.RewardWallets.Add(wallet);
        await context.SaveChangesAsync();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RedeemPointsAsync(userId, redeemAmount, orderId));

        exception.Message.Should().Contain("Insufficient reward balance");
        exception.Message.Should().Contain("30");
        exception.Message.Should().Contain("50");
    }

    [Fact]
    public async Task RedeemPointsAsync_WithNonExistentWallet_ThrowsKeyNotFoundException()
    {
        // Arrange
        var context = GetInMemoryDbContext();
        var loggerMock = new Mock<ILogger<RewardService>>();
        var service = new RewardService(context);

        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.RedeemPointsAsync(userId, 50m, orderId));

        exception.Message.Should().Contain("Reward wallet not found");
    }

    }
