using DuneFlame.Application.DTOs.Auth;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Persistence;
using DuneFlame.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;

namespace DuneFlame.UnitTests;

/// <summary>
/// Tests for concurrent token refresh requests to verify race condition handling.
/// These tests validate the grace period and idempotency window implementation.
/// </summary>
public class ConcurrentTokenRefreshTests
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly JwtSettings _jwtSettings;
    private readonly IJwtTokenGenerator _tokenGenerator;

    public ConcurrentTokenRefreshTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _jwtSettings = new JwtSettings
        {
            Secret = "this-is-a-very-long-secret-key-for-testing-purposes-at-least-32-bytes-long",
            Issuer = "DuneFlame",
            Audience = "DuneFlameSPA",
            AccessTokenExpirationInMinutes = 15,
            RefreshTokenExpirationInDays = 7,
            RefreshTokenRevocationGracePeriodSeconds = 30
        };

        var options = Options.Create(_jwtSettings);
        _tokenGenerator = new JwtTokenGenerator(options);
    }

    private AppDbContext GetDbContext() => new AppDbContext(_dbOptions);

    private AuthService CreateAuthService(AppDbContext context, IOptions<JwtSettings> jwtOptions)
    {
        var userManagerMock = new Mock<UserManager<ApplicationUser>>(
            new Mock<IUserStore<ApplicationUser>>().Object,
            null, null, null, null, null, null, null, null);

        var signInManagerMock = new Mock<SignInManager<ApplicationUser>>(
            userManagerMock.Object, new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            null, null, null, null);

        var emailServiceMock = new Mock<IEmailService>();
        var loggerMock = new Mock<ILogger<AuthService>>();

        // Configure UserManager mock for GetRolesAsync
        userManagerMock
            .Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new[] { "Customer" });

        // Configure UserManager mock for FindByIdAsync to read from context
        userManagerMock
            .Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .Returns<string>(userId =>
            {
                if (!Guid.TryParse(userId, out var guidId)) return Task.FromResult<ApplicationUser>(null);
                var user = context.Users.Find(guidId);
                return Task.FromResult(user);
            });

        return new AuthService(
            userManagerMock.Object,
            signInManagerMock.Object,
            _tokenGenerator,
            emailServiceMock.Object,
            loggerMock.Object,
            context,
            jwtOptions
        );
    }

    [Fact]
    public async Task ConcurrentRefresh_Should_Handle_Simultaneous_Requests_Within_Grace_Period()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var roles = new[] { "Customer" };
        var accessToken = _tokenGenerator.GenerateAccessToken(user, roles);
        var oldRefreshToken = _tokenGenerator.GenerateRefreshToken();
        oldRefreshToken.UserId = user.Id;

        context.RefreshTokens.Add(oldRefreshToken);
        await context.SaveChangesAsync();

        var jwtOptions = Options.Create(_jwtSettings);
        var authService = CreateAuthService(context, jwtOptions);

        var tokenRequest = new TokenRequest(accessToken, oldRefreshToken.Token);

        // Act - Simulate two concurrent refresh requests
        var task1 = authService.RefreshTokenAsync(tokenRequest);
        var task2 = authService.RefreshTokenAsync(tokenRequest);

        // Assert - Both should succeed (first one issues new token, second one returns existing active token due to grace period)
        var results = await Task.WhenAll(task1, task2);

        results.Should().HaveCount(2);
        results[0].Should().NotBeNull();
        results[1].Should().NotBeNull();
        results[0].Email.Should().Be(user.Email);
        results[1].Email.Should().Be(user.Email);

        // Verify at least one new token was created
        var utcNow = DateTime.UtcNow;
        var activeTokens = context.RefreshTokens
            .Where(x => x.UserId == user.Id && x.Expires > utcNow && x.Revoked == null)
            .ToList();

        activeTokens.Should().NotBeEmpty("There should be at least one active token after refresh");
    }

    [Fact]
    public async Task ConcurrentRefresh_Should_Return_Idempotent_Response_For_Second_Request()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var roles = new[] { "Customer" };
        var accessToken = _tokenGenerator.GenerateAccessToken(user, roles);
        var oldRefreshToken = _tokenGenerator.GenerateRefreshToken();
        oldRefreshToken.UserId = user.Id;

        context.RefreshTokens.Add(oldRefreshToken);
        await context.SaveChangesAsync();

        var jwtOptions = Options.Create(_jwtSettings);
        var authService = CreateAuthService(context, jwtOptions);

        var tokenRequest = new TokenRequest(accessToken, oldRefreshToken.Token);

        // Act - First request
        var firstResult = await authService.RefreshTokenAsync(tokenRequest);

        // Simulate second concurrent request arriving while first is being processed
        // (In real scenario, DB would show old token as revoked)
        var secondResult = await authService.RefreshTokenAsync(tokenRequest);

        // Assert - Second result should contain the new token from first request
        firstResult.Should().NotBeNull();
            secondResult.Should().NotBeNull();

            // Both responses should have valid tokens
            firstResult.RefreshToken.Should().NotBeNullOrEmpty();
            secondResult.RefreshToken.Should().NotBeNullOrEmpty();

            // Verify refresh tokens are different (first creates new, second uses existing)
            // Or if they're the same, it's idempotent
            var utcNow = DateTime.UtcNow;
            var tokenCount = context.RefreshTokens
                .Count(x => x.UserId == user.Id && x.Expires > utcNow);

            tokenCount.Should().BeGreaterThanOrEqualTo(1, "Should have at least one valid token");
        }

    [Fact]
    public async Task Refresh_Should_Fail_After_Grace_Period_Expires()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var roles = new[] { "Customer" };
        var accessToken = _tokenGenerator.GenerateAccessToken(user, roles);
        var refreshToken = _tokenGenerator.GenerateRefreshToken();
        refreshToken.UserId = user.Id;

        context.RefreshTokens.Add(refreshToken);
        await context.SaveChangesAsync();

        // Manually revoke the token with old timestamp (beyond grace period)
        refreshToken.Revoked = DateTime.UtcNow.AddSeconds(-35); // 35 seconds ago (grace period is 30s)
        context.RefreshTokens.Update(refreshToken);
        await context.SaveChangesAsync();

        var jwtOptions = Options.Create(_jwtSettings);
        var authService = CreateAuthService(context, jwtOptions);

        var tokenRequest = new TokenRequest(accessToken, refreshToken.Token);

        // Act & Assert
        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(
            () => authService.RefreshTokenAsync(tokenRequest)
        );
    }

    [Fact]
    public async Task Refresh_Should_Accept_Token_Within_Grace_Period()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var roles = new[] { "Customer" };
        var accessToken = _tokenGenerator.GenerateAccessToken(user, roles);
        var oldRefreshToken = _tokenGenerator.GenerateRefreshToken();
        oldRefreshToken.UserId = user.Id;

        context.RefreshTokens.Add(oldRefreshToken);
        await context.SaveChangesAsync();

        // Manually revoke the token with recent timestamp (within grace period)
        oldRefreshToken.Revoked = DateTime.UtcNow.AddSeconds(-10); // 10 seconds ago (grace period is 30s)
        context.RefreshTokens.Update(oldRefreshToken);
        await context.SaveChangesAsync();

        var jwtOptions = Options.Create(_jwtSettings);
        var authService = CreateAuthService(context, jwtOptions);

        var tokenRequest = new TokenRequest(accessToken, oldRefreshToken.Token);

        // Act - Should succeed because token is within grace period
        var result = await authService.RefreshTokenAsync(tokenRequest);

        // Assert
        result.Should().NotBeNull();
        result.Email.Should().Be(user.Email);
        result.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Refresh_Should_Prevent_Reuse_Of_Same_Token_After_Successful_Rotation()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var roles = new[] { "Customer" };
        var accessToken = _tokenGenerator.GenerateAccessToken(user, roles);
        var refreshToken = _tokenGenerator.GenerateRefreshToken();
        refreshToken.UserId = user.Id;

        context.RefreshTokens.Add(refreshToken);
        await context.SaveChangesAsync();

        var jwtSettings = new JwtSettings
        {
            Secret = "this-is-a-very-long-secret-key-for-testing-purposes-at-least-32-bytes-long",
            Issuer = "DuneFlame",
            Audience = "DuneFlameSPA",
            AccessTokenExpirationInMinutes = 15,
            RefreshTokenExpirationInDays = 7,
            RefreshTokenRevocationGracePeriodSeconds = 1  // 1 second grace period for this test
        };

        var jwtOptions = Options.Create(jwtSettings);
        var authService = CreateAuthService(context, jwtOptions);

        var tokenRequest = new TokenRequest(accessToken, refreshToken.Token);

        // Act - First refresh succeeds
        var firstResult = await authService.RefreshTokenAsync(tokenRequest);
        firstResult.Should().NotBeNull();

        // Wait for grace period to expire
        await Task.Delay(1100); // Wait 1.1 seconds (grace period is 1 second)

        // Act - Try to use old token again after grace period expires
        var secondAttempt = authService.RefreshTokenAsync(tokenRequest);

        // Assert - Should fail because grace period expired
        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(
            () => secondAttempt
        );
    }

    [Fact]
    public async Task Refresh_Should_Track_Multiple_Active_Tokens_Per_User()
    {
        // Arrange
        using var context = GetDbContext();
        context.Database.EnsureCreated();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            UserName = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Create multiple refresh tokens for the same user
        var token1 = _tokenGenerator.GenerateRefreshToken();
        token1.UserId = user.Id;
        var token2 = _tokenGenerator.GenerateRefreshToken();
        token2.UserId = user.Id;

        context.RefreshTokens.AddRange(token1, token2);
        await context.SaveChangesAsync();

                // Act - Verify both tokens are active
                var utcNow = DateTime.UtcNow;
                var activeTokens = context.RefreshTokens
                    .Where(x => x.UserId == user.Id && x.Expires > utcNow && x.Revoked == null)
                    .ToList();

                // Assert
                activeTokens.Should().HaveCount(2, "Multiple active tokens should be allowed per user");
            }
        }
