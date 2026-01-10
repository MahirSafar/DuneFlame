using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace DuneFlame.UnitTests;

public class JwtTokenGeneratorTests
{
    private readonly JwtTokenGenerator _tokenGenerator;

    public JwtTokenGeneratorTests()
    {
        var jwtSettings = new JwtSettings
        {
            Secret = "this-is-a-very-long-secret-key-for-testing-purposes-at-least-32-bytes-long",
            Issuer = "DuneFlame",
            Audience = "DuneFlameSPA",
            AccessTokenExpirationInMinutes = 15,
            RefreshTokenExpirationInDays = 7
        };

        var options = Options.Create(jwtSettings);
        _tokenGenerator = new JwtTokenGenerator(options);
    }

    [Fact]
    public void GenerateAccessToken_Should_Create_Valid_Token()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com"
        };
        var roles = new List<string> { "Customer" };

        // Act
        var token = _tokenGenerator.GenerateAccessToken(user, roles);

        // Assert
        token.Should().NotBeNullOrEmpty();
        token.Split('.').Should().HaveCount(3); // JWT format: header.payload.signature
    }

    [Fact]
    public void GenerateRefreshToken_Should_Create_Token_With_Expiration()
    {
        // Act
        var refreshToken = _tokenGenerator.GenerateRefreshToken();

        // Assert
        refreshToken.Should().NotBeNull();
        refreshToken.Token.Should().NotBeNullOrEmpty();
        refreshToken.Expires.Should().BeAfter(DateTime.UtcNow);
        refreshToken.IsActive.Should().BeTrue();
        refreshToken.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_Should_Extract_Claims_From_Valid_Token()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com"
        };
        var roles = new List<string> { "Customer" };
        var token = _tokenGenerator.GenerateAccessToken(user, roles);

        // Act
        var principal = _tokenGenerator.GetPrincipalFromExpiredToken(token);

        // Assert
        principal.Should().NotBeNull();
        var emailClaim = principal.Claims.FirstOrDefault(c => c.Type == "email");
        emailClaim?.Value.Should().Be("test@example.com");
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_Should_Return_Null_For_Invalid_Token()
    {
        // Act
        var principal = _tokenGenerator.GetPrincipalFromExpiredToken("invalid-token");

        // Assert
        principal.Should().BeNull();
    }
}

public class RefreshTokenEntityTests
{
    [Fact]
    public void RefreshToken_IsExpired_Should_Return_True_When_Expires_Time_Passed()
    {
        // Arrange
        var token = new RefreshToken
        {
            Token = "test-token",
            Expires = DateTime.UtcNow.AddHours(-1) // 1 hour ago
        };

        // Act & Assert
        token.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void RefreshToken_IsExpired_Should_Return_False_When_Still_Valid()
    {
        // Arrange
        var token = new RefreshToken
        {
            Token = "test-token",
            Expires = DateTime.UtcNow.AddHours(1) // 1 hour from now
        };

        // Act & Assert
        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void RefreshToken_IsActive_Should_Return_False_When_Revoked()
    {
        // Arrange
        var token = new RefreshToken
        {
            Token = "test-token",
            Expires = DateTime.UtcNow.AddHours(1),
            Revoked = DateTime.UtcNow
        };

        // Act & Assert
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void RefreshToken_IsActive_Should_Return_True_When_Valid()
    {
        // Arrange
        var token = new RefreshToken
        {
            Token = "test-token",
            Expires = DateTime.UtcNow.AddHours(1),
            Revoked = null
        };

        // Act & Assert
        token.IsActive.Should().BeTrue();
    }
}

public class PasswordResetTokenEntityTests
{
    [Fact]
    public void PasswordResetToken_IsExpired_Should_Return_True_When_Expires_Time_Passed()
    {
        // Arrange
        var token = new PasswordResetToken
        {
            Token = "reset-token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        };

        // Act & Assert
        token.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void PasswordResetToken_IsExpired_Should_Return_False_When_Still_Valid()
    {
        // Arrange
        var token = new PasswordResetToken
        {
            Token = "reset-token",
            ExpiresAt = DateTime.UtcNow.AddHours(23)
        };

        // Act & Assert
        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void PasswordResetToken_IsUsed_Should_Return_True_When_UsedAt_Is_Set()
    {
        // Arrange
        var token = new PasswordResetToken
        {
            Token = "reset-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UsedAt = DateTime.UtcNow
        };

        // Act & Assert
        token.IsUsed.Should().BeTrue();
    }

    [Fact]
    public void PasswordResetToken_IsUsed_Should_Return_False_When_UsedAt_Is_Null()
    {
        // Arrange
        var token = new PasswordResetToken
        {
            Token = "reset-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UsedAt = null
        };

        // Act & Assert
        token.IsUsed.Should().BeFalse();
    }
}

public class ExternalLoginEntityTests
{
    [Fact]
    public void ExternalLogin_Should_Store_Provider_Information()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var externalLogin = new ExternalLogin
        {
            UserId = userId,
            LoginProvider = "Google",
            ProviderKey = "google-user-123",
            ProviderDisplayName = "Google User",
            ConnectedAt = DateTime.UtcNow
        };

        // Act & Assert
        externalLogin.UserId.Should().Be(userId);
        externalLogin.LoginProvider.Should().Be("Google");
        externalLogin.ProviderKey.Should().Be("google-user-123");
    }
}

public class ApplicationUserEntityTests
{
    [Fact]
    public void ApplicationUser_Should_Have_Empty_Collections_On_Initialization()
    {
        // Arrange & Act
        var user = new ApplicationUser
        {
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        // Assert
        user.RefreshTokens.Should().BeEmpty();
        user.ExternalLogins.Should().BeEmpty();
        user.PasswordResetTokens.Should().BeEmpty();
    }

    [Fact]
    public void ApplicationUser_Should_Support_Multiple_RefreshTokens()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        };

        var token1 = new RefreshToken { Token = "token1", Expires = DateTime.UtcNow.AddDays(7) };
        var token2 = new RefreshToken { Token = "token2", Expires = DateTime.UtcNow.AddDays(7) };

        // Act
        user.RefreshTokens.Add(token1);
        user.RefreshTokens.Add(token2);

        // Assert
        user.RefreshTokens.Should().HaveCount(2);
    }
}

public class DTOValidationTests
{
    [Fact]
    public void ForgotPasswordRequest_Should_Be_Created_With_Email()
    {
        // Arrange & Act
        var request = new DuneFlame.Application.DTOs.Auth.ForgotPasswordRequest("test@example.com");

        // Assert
        request.Email.Should().Be("test@example.com");
    }

    [Fact]
    public void ResetPasswordRequest_Should_Be_Created_With_Required_Fields()
    {
        // Arrange & Act
        var request = new DuneFlame.Application.DTOs.Auth.ResetPasswordRequest(
            "test@example.com",
            "reset-token",
            "NewPassword123!",
            "NewPassword123!"
        );

        // Assert
        request.Email.Should().Be("test@example.com");
        request.Token.Should().Be("reset-token");
        request.NewPassword.Should().Be("NewPassword123!");
        request.ConfirmPassword.Should().Be("NewPassword123!");
    }
}
