using DuneFlame.Application.DTOs.Auth;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Authentication;
using System.Security.Claims;

namespace DuneFlame.Infrastructure.Services;

public class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IEmailService emailService,
    ILogger<AuthService> logger,
    AppDbContext context) : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
    private readonly IJwtTokenGenerator _jwtTokenGenerator = jwtTokenGenerator;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger _logger = logger;
    private readonly AppDbContext _context = context;

    public async Task<bool> RegisterAsync(RegisterRequest request)
    {
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
            throw new ConflictException("User with this email already exists.");

        var user = new ApplicationUser
        {
            Email = request.Email,
            UserName = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            EmailConfirmed = false
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException($"Registration failed: {errors}");
        }

        await _userManager.AddToRoleAsync(user, "Customer");

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendVerificationEmailAsync(user.Email!, user.Id.ToString(), token);

        return true;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email) ?? throw new AuthenticationException("Invalid credentials.");

        // ƏLAVƏ: Email təsdiqlənibmi?
        if (!user.EmailConfirmed)
        {
            throw new AuthenticationException("Please confirm your email before logging in.");
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            throw new AuthenticationException("Account is locked due to multiple failed login attempts. Try again in 15 minutes.");
        }

        if (!result.Succeeded)
        {
            throw new AuthenticationException("Invalid credentials.");
        }

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> RefreshTokenAsync(TokenRequest request)
    {
        // 1. Token validasiyası (Vaxtı bitmiş olsa belə, formatı düzdür?)
        var principal = _jwtTokenGenerator.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null) throw new AuthenticationException("Invalid token.");

        var userId = principal.Claims.FirstOrDefault(c => c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var user = await _userManager.FindByIdAsync(userId!);

        if (user == null) throw new AuthenticationException("User not found.");

        // 2. Refresh token bazada varmı və aktivdirmi?
        var storedRefreshToken = await _context.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Token == request.RefreshToken && x.UserId == user.Id);

        if (storedRefreshToken == null)
            throw new AuthenticationException("Refresh token not found.");

        if (storedRefreshToken.User == null)
            throw new AuthenticationException("Invalid refresh token state.");

        if (!storedRefreshToken.IsActive)
            throw new AuthenticationException("Invalid or expired refresh token.");

        // 3. Köhnə tokeni ləğv et (Revoke)
        storedRefreshToken.Revoked = DateTime.UtcNow;
        _context.RefreshTokens.Update(storedRefreshToken);
        await _context.SaveChangesAsync();

        // 4. Yeni tokenləri yarat
        return await GenerateAuthResponseAsync(user);
    }

    public async Task LogoutAsync(string userId)
    {
        // Real ssenaridə burada aktiv refresh tokenləri ləğv etmək olar.
        // Hələlik sadə saxlayırıq.
        await Task.CompletedTask;
    }

    // Helper metod: Tokenləri yaradıb bazaya yazır və Response qaytarır
    private async Task<AuthResponse> GenerateAuthResponseAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _jwtTokenGenerator.GenerateAccessToken(user, roles);
        var refreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        // Refresh tokeni istifadəçiyə bağlayıb bazada saxlayırıq
        refreshToken.UserId = user.Id;
        await _context.RefreshTokens.AddAsync(refreshToken);
        await _context.SaveChangesAsync();

        return new AuthResponse(
            user.Id,
            user.Email!,
            user.FirstName,
            user.LastName,
            accessToken,
            refreshToken.Token,
            roles.ToList()
        );
    }
    public async Task<bool> VerifyEmailAsync(string userId, string token)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        // Identity avtomatik olaraq DB-dəki tokeni yoxlayır
        var result = await _userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded;
    }
    public async Task<AuthenticationProperties> ConfigureExternalLoginsAsync(string provider, string redirectUrl)
    {
        // Google login pəncərəsini açmaq üçün parametrlər
        return _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
    }

    public async Task<AuthResponse> ExternalLoginCallbackAsync()
    {
        // Google-dan gələn məlumatı oxu
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null) throw new BadRequestException("Error loading external login information.");

        // Bu Google hesabı ilə əvvəl giriş edilibmi?
        var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            return await GenerateAuthResponseAsync(user!);
        }

        // Əgər istifadəçi yoxdursa, yeni yaradılır (Auto-create)
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var userByEmail = await _userManager.FindByEmailAsync(email!);

        if (userByEmail == null)
        {
            userByEmail = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = info.Principal.FindFirstValue(ClaimTypes.GivenName) ?? "Google",
                LastName = info.Principal.FindFirstValue(ClaimTypes.Surname) ?? "User",
                EmailConfirmed = true // Google artıq təsdiqləyib
            };
            await _userManager.CreateAsync(userByEmail);
            await _userManager.AddToRoleAsync(userByEmail, "Customer");
        }

                // Google məlumatını bizim User-ə bağla (ExternalLogins cədvəlinə yazılır)
                await _userManager.AddLoginAsync(userByEmail, info);

                return await GenerateAuthResponseAsync(userByEmail);
            }

            public async Task<bool> ForgotPasswordAsync(string email)
            {
                var user = await _userManager.FindByEmailAsync(email);
                if (user == null)
                    return false; // Don't reveal if email exists for security

                // Generate reset token using Identity
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                // Store token in database for tracking
                var resetToken = new PasswordResetToken
                {
                    UserId = user.Id,
                    Token = token,
                    ExpiresAt = DateTime.UtcNow.AddHours(24)
                };

                await _context.PasswordResetTokens.AddAsync(resetToken);
                await _context.SaveChangesAsync();

                try
                {
                    await _emailService.SendPasswordResetEmailAsync(user.Email!, user.Id.ToString(), token);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Password reset email could not be sent to {Email}", user.Email);
                    return false;
                }
            }

            public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
            {
                var user = await _userManager.FindByEmailAsync(email);
                if (user == null)
                    throw new AuthenticationException("User not found.");

                // Validate reset token exists and is not expired/used
                var resetToken = await _context.PasswordResetTokens
                    .FirstOrDefaultAsync(rt => rt.UserId == user.Id && rt.Token == token);

                if (resetToken == null || resetToken.IsExpired || resetToken.IsUsed)
                    throw new AuthenticationException("Invalid or expired password reset token.");

                // Reset password
                var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    throw new AuthenticationException($"Password reset failed: {errors}");
                }

                // Mark token as used
                resetToken.UsedAt = DateTime.UtcNow;
                _context.PasswordResetTokens.Update(resetToken);
                await _context.SaveChangesAsync();

                return true;
            }
        }