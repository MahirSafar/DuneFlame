using DuneFlame.Application.DTOs.Auth;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using DuneFlame.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DuneFlame.UnitTests;

public class PasswordResetTokenStorageTests
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public PasswordResetTokenStorageTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    private AppDbContext GetDbContext() => new AppDbContext(_dbOptions);

    [Fact]
    public async Task PasswordResetToken_Should_Be_Stored_And_Retrieved()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var resetToken = new PasswordResetToken
        {
            UserId = userId,
            Token = "reset-token-123",
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        context.PasswordResetTokens.Add(resetToken);
        await context.SaveChangesAsync();

        // Act
        var storedToken = context.PasswordResetTokens.FirstOrDefault(rt => rt.Token == "reset-token-123");

        // Assert
        storedToken.Should().NotBeNull();
        storedToken!.UserId.Should().Be(userId);
        storedToken.IsExpired.Should().BeFalse();
        storedToken.IsUsed.Should().BeFalse();
    }

    [Fact]
    public async Task PasswordResetToken_Should_Support_Expiration_Check()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var expiredToken = new PasswordResetToken
        {
            UserId = userId,
            Token = "expired-token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        };

        context.PasswordResetTokens.Add(expiredToken);
        await context.SaveChangesAsync();

        // Act
        var retrievedToken = context.PasswordResetTokens.FirstOrDefault(rt => rt.Token == "expired-token");

        // Assert
        retrievedToken.Should().NotBeNull();
        retrievedToken!.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task PasswordResetToken_Should_Track_Usage()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var resetToken = new PasswordResetToken
        {
            UserId = userId,
            Token = "reset-token-456",
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            UsedAt = null
        };

        context.PasswordResetTokens.Add(resetToken);
        await context.SaveChangesAsync();

        // Act - Mark as used
        var token = context.PasswordResetTokens.First(rt => rt.Token == "reset-token-456");
        token.UsedAt = DateTime.UtcNow;
        context.PasswordResetTokens.Update(token);
        await context.SaveChangesAsync();

        // Assert
        var usedToken = context.PasswordResetTokens.First(rt => rt.Token == "reset-token-456");
        usedToken.IsUsed.Should().BeTrue();
    }
}

// Mock logger for testing
public class MockLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
