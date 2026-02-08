using DuneFlame.Application.DTOs.Auth;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Exceptions;
using DuneFlame.Infrastructure.Authentication;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Authentication;
using System.Security.Claims;

namespace DuneFlame.Infrastructure.Services;

public class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IJwtTokenGenerator jwtTokenGenerator,
    IEmailService emailService,
    ILogger<AuthService> logger,
    AppDbContext context,
    IOptions<JwtSettings> jwtSettings) : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
    private readonly IJwtTokenGenerator _jwtTokenGenerator = jwtTokenGenerator;
    private readonly IEmailService _emailService = emailService;
    private readonly ILogger _logger = logger;
    private readonly AppDbContext _context = context;
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;

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
        _logger.LogInformation("[REFRESH-SERVICE] Step 1: Validating AccessToken format and signature...");

        // 1. Token validasiyası (Vaxtı bitmiş olsa belə, formatı düzdür?)
        ClaimsPrincipal? principal = null;
        try
        {
            principal = _jwtTokenGenerator.GetPrincipalFromExpiredToken(request.AccessToken);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogError("[REFRESH-SERVICE] AccessToken validation failed with SecurityTokenException: {ErrorMessage}", ex.Message);
            throw new AuthenticationException($"Invalid token: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError("[REFRESH-SERVICE] AccessToken validation failed with unexpected exception: {ExceptionType} - {ErrorMessage}", ex.GetType().Name, ex.Message);
            throw new AuthenticationException("Invalid token.");
        }

        if (principal == null)
        {
            _logger.LogError("[REFRESH-SERVICE] AccessToken validation failed - GetPrincipalFromExpiredToken returned null. Token format or signature may be invalid.");
            throw new AuthenticationException("Invalid token.");
        }

        _logger.LogInformation("[REFRESH-SERVICE] AccessToken format is valid. Extracting user ID from claims...");

        // Check for userId in both Sub (JWT standard) and NameIdentifier (ASP.NET Identity standard)
        var userId = principal.Claims.FirstOrDefault(c => c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value 
                     ?? principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("[REFRESH-SERVICE] UserId extraction failed. Available claims: {Claims}", 
                string.Join(", ", principal.Claims.Select(c => $"{c.Type}={c.Value}")));
            throw new AuthenticationException("User not found.");
        }

        _logger.LogInformation("[REFRESH-SERVICE] Step 2: Looking up user in database. UserId: {UserId}", userId);

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            _logger.LogError("[REFRESH-SERVICE] User lookup failed. UserId not found in database: {UserId}", userId);
            throw new AuthenticationException("User not found.");
        }

        _logger.LogInformation("[REFRESH-SERVICE] User found in database: {Email}. Step 3: Validating refresh token in database...", user.Email);

        // 2. Refresh token bazada varmı?
        var utcNow = DateTime.UtcNow;
        var storedRefreshToken = await _context.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Token == request.RefreshToken && x.UserId == user.Id);

        if (storedRefreshToken == null)
        {
            _logger.LogWarning("[REFRESH-SERVICE] Refresh token not found in database for UserId: {UserId}. " +
                "This could mean: (1) Token was never created, (2) Token was revoked, (3) Token hash mismatch, (4) UserId mismatch", 
                user.Id);

            // Additional debugging: count how many refresh tokens exist for this user
            var tokenCount = await _context.RefreshTokens.CountAsync(x => x.UserId == user.Id);
            _logger.LogWarning("[REFRESH-SERVICE] Total refresh tokens in DB for this user: {TokenCount}", tokenCount);

            throw new AuthenticationException("Refresh token not found.");
        }

        _logger.LogInformation("[REFRESH-SERVICE] Refresh token found in database. Checking validity...");

        if (storedRefreshToken.User == null)
        {
            _logger.LogError("[REFRESH-SERVICE] Database integrity issue: Stored refresh token has null User reference for UserId: {UserId}", user.Id);
            throw new AuthenticationException("Invalid refresh token state.");
        }

        // 3. Check if token is active or within grace period after revocation
        var gracePeriod = TimeSpan.FromSeconds(_jwtSettings.RefreshTokenRevocationGracePeriodSeconds);
        bool isWithinGracePeriod = storedRefreshToken.Revoked.HasValue && 
                                   (utcNow - storedRefreshToken.Revoked.Value) < gracePeriod;

        _logger.LogInformation("[REFRESH-SERVICE] Token validity check - " +
            "Expires: {Expires} (UTC), Now: {Now} (UTC), IsExpired: {IsExpired}, " +
            "Revoked: {Revoked}, IsRevoked: {IsRevoked}, WithinGracePeriod: {WithinGracePeriod}, " +
            "IsActive: {IsActive}, GracePeriod: {GracePeriodSeconds}s",
            storedRefreshToken.Expires,
            utcNow,
            storedRefreshToken.IsExpired,
            storedRefreshToken.Revoked,
            storedRefreshToken.Revoked.HasValue,
            isWithinGracePeriod,
            storedRefreshToken.IsActive,
            _jwtSettings.RefreshTokenRevocationGracePeriodSeconds);

        if (!storedRefreshToken.IsActive && !isWithinGracePeriod)
        {
            string failureReason = storedRefreshToken.IsExpired 
                ? $"Token expired at {storedRefreshToken.Expires:O}" 
                : $"Token was revoked at {storedRefreshToken.Revoked:O} (outside {_jwtSettings.RefreshTokenRevocationGracePeriodSeconds}s grace period)";

            _logger.LogWarning("[REFRESH-SERVICE] Token rejected - Reason: {FailureReason}", failureReason);
            throw new AuthenticationException("Invalid or expired refresh token.");
        }

        // 4. If token was already revoked (within grace period), check if a newer token exists
        if (storedRefreshToken.Revoked.HasValue && isWithinGracePeriod)
        {
            _logger.LogInformation("[REFRESH-SERVICE] Refresh token reuse detected within grace period for UserId: {UserId}. " +
                "Checking for replacement token (idempotent response)...", user.Id);

            // Get the most recent active refresh token for this user
            var newestToken = await _context.RefreshTokens
                .Where(x => x.UserId == user.Id && x.Expires > utcNow && x.Revoked == null)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (newestToken != null)
            {
                _logger.LogInformation("[REFRESH-SERVICE] Found replacement active token. Returning existing token (idempotent) for UserId: {UserId}", user.Id);

                // Return the newest active token for this user (idempotent response)
                var roles = await _userManager.GetRolesAsync(user);
                var accessToken = _jwtTokenGenerator.GenerateAccessToken(user, roles);

                return new AuthResponse(
                    user.Id,
                    user.Email!,
                    user.FirstName,
                    user.LastName,
                    accessToken,
                    newestToken.Token,
                    roles.ToList()
                );
            }
            else
            {
                _logger.LogWarning("[REFRESH-SERVICE] No replacement active token found. Will proceed to issue new token.");
            }
        }

        // 5. Token is valid and active - revoke it and issue new tokens
        _logger.LogInformation("[REFRESH-SERVICE] Step 4: Token is valid. Revoking old token and issuing new ones for UserId: {UserId}", user.Id);

        storedRefreshToken.Revoked = utcNow;
        _context.RefreshTokens.Update(storedRefreshToken);
        await _context.SaveChangesAsync();

        _logger.LogInformation("[REFRESH-SERVICE] Old token revoked. Step 5: Generating new tokens...");

        // 6. Generate new tokens
        var response = await GenerateAuthResponseAsync(user);
        _logger.LogInformation("[REFRESH-SERVICE] Token refresh completed successfully for UserId: {UserId}", user.Id);
        return response;
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