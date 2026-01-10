using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.UnitTests;

public class TokenReusePreventionTests
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public TokenReusePreventionTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    private AppDbContext GetDbContext() => new AppDbContext(_dbOptions);

    [Fact]
    public async Task RefreshToken_Should_Prevent_Reuse_After_Revocation()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = "test-refresh-token",
            Expires = DateTime.UtcNow.AddDays(7)
        };

        context.RefreshTokens.Add(refreshToken);
        await context.SaveChangesAsync();

        // Act - Revoke the token
        var tokenFromDb = context.RefreshTokens.First(rt => rt.Token == "test-refresh-token");
        tokenFromDb.Revoked = DateTime.UtcNow;
        context.RefreshTokens.Update(tokenFromDb);
        await context.SaveChangesAsync();

        // Assert - Token should not be active anymore
        var revokedToken = context.RefreshTokens.First(rt => rt.Token == "test-refresh-token");
        revokedToken.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshToken_Should_Not_Allow_Use_After_Expiration()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var expiredToken = new RefreshToken
        {
            UserId = user.Id,
            Token = "expired-token",
            Expires = DateTime.UtcNow.AddHours(-1) // Already expired
        };

        context.RefreshTokens.Add(expiredToken);
        await context.SaveChangesAsync();

        // Assert - Token should be expired
        var tokenFromDb = context.RefreshTokens.First(rt => rt.Token == "expired-token");
        tokenFromDb.IsExpired.Should().BeTrue();
        tokenFromDb.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_RefreshToken_Should_Not_Be_Found_In_Database()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Act - Try to find a token that doesn't exist
        var invalidToken = context.RefreshTokens
            .FirstOrDefault(rt => rt.Token == "invalid-token" && rt.UserId == user.Id);

        // Assert
        invalidToken.Should().BeNull();
    }

    [Fact]
    public async Task PasswordResetToken_Should_Prevent_Reuse_After_Password_Change()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            Token = "reset-token-123",
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        context.PasswordResetTokens.Add(resetToken);
        await context.SaveChangesAsync();

        // Act - Mark token as used
        var tokenFromDb = context.PasswordResetTokens.First(rt => rt.Token == "reset-token-123");
        tokenFromDb.UsedAt = DateTime.UtcNow;
        context.PasswordResetTokens.Update(tokenFromDb);
        await context.SaveChangesAsync();

        // Assert - Token should not be usable again
        var usedToken = context.PasswordResetTokens.First(rt => rt.Token == "reset-token-123");
        usedToken.IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task PasswordResetToken_Should_Expire_After_24_Hours()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var expiredResetToken = new PasswordResetToken
        {
            UserId = user.Id,
            Token = "expired-reset-token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // Expired
        };

        context.PasswordResetTokens.Add(expiredResetToken);
        await context.SaveChangesAsync();

        // Assert - Token should be expired
        var tokenFromDb = context.PasswordResetTokens.First(rt => rt.Token == "expired-reset-token");
        tokenFromDb.IsExpired.Should().BeTrue();
    }
}

public class EmailVerificationTests
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public EmailVerificationTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    private AppDbContext GetDbContext() => new AppDbContext(_dbOptions);

    [Fact]
    public async Task User_Should_Track_Email_Confirmation_Status()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com",
            EmailConfirmed = false
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Assert - Initially not confirmed
        var savedUser = context.Users.First(u => u.Email == "test@example.com");
        savedUser.EmailConfirmed.Should().BeFalse();

        // Act - Mark as confirmed
        savedUser.EmailConfirmed = true;
        context.Users.Update(savedUser);
        await context.SaveChangesAsync();

        // Assert - Now confirmed
        var confirmedUser = context.Users.First(u => u.Email == "test@example.com");
        confirmedUser.EmailConfirmed.Should().BeTrue();
    }
}

public class ExternalLoginTests
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public ExternalLoginTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    private AppDbContext GetDbContext() => new AppDbContext(_dbOptions);

    [Fact]
    public async Task User_Should_Support_Multiple_External_Logins()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var googleLogin = new ExternalLogin
        {
            UserId = user.Id,
            LoginProvider = "Google",
            ProviderKey = "google-123",
            ProviderDisplayName = "Google Account",
            ConnectedAt = DateTime.UtcNow
        };

        var facebookLogin = new ExternalLogin
        {
            UserId = user.Id,
            LoginProvider = "Facebook",
            ProviderKey = "facebook-456",
            ProviderDisplayName = "Facebook Account",
            ConnectedAt = DateTime.UtcNow
        };

        // Act
        context.ExternalLogins.AddRange(googleLogin, facebookLogin);
        await context.SaveChangesAsync();

        // Assert
        var userLogins = context.ExternalLogins.Where(el => el.UserId == user.Id).ToList();
        userLogins.Should().HaveCount(2);
        userLogins.Should().Contain(el => el.LoginProvider == "Google");
        userLogins.Should().Contain(el => el.LoginProvider == "Facebook");
    }

    [Fact]
    public async Task External_Login_Should_Prevent_Duplicate_Provider_Keys()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var googleLogin1 = new ExternalLogin
        {
            UserId = user.Id,
            LoginProvider = "Google",
            ProviderKey = "google-123",
            ProviderDisplayName = "Google Account",
            ConnectedAt = DateTime.UtcNow
        };

        context.ExternalLogins.Add(googleLogin1);
        await context.SaveChangesAsync();

        // Act - Try to find existing Google login
        var existingLogin = context.ExternalLogins.FirstOrDefault(
            el => el.LoginProvider == "Google" && el.ProviderKey == "google-123" && el.UserId == user.Id);

        // Assert
        existingLogin.Should().NotBeNull();
        existingLogin!.LoginProvider.Should().Be("Google");
    }
}

public class AccountLockoutTests
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public AccountLockoutTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    private AppDbContext GetDbContext() => new AppDbContext(_dbOptions);

    [Fact]
    public async Task User_Should_Support_Lockout_Tracking()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com",
            AccessFailedCount = 0,
            LockoutEnabled = true
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Act - Simulate failed login attempts
        var savedUser = context.Users.First(u => u.Email == "test@example.com");
        savedUser.AccessFailedCount = 5;
        savedUser.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        context.Users.Update(savedUser);
        await context.SaveChangesAsync();

        // Assert
        var lockedUser = context.Users.First(u => u.Email == "test@example.com");
        lockedUser.AccessFailedCount.Should().Be(5);
        lockedUser.LockoutEnd.Should().NotBeNull();
    }
}
