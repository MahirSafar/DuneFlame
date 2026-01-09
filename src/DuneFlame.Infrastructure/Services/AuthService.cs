using System.Security.Authentication;
using DuneFlame.Application.DTOs.Auth;
using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Services;

public class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IJwtTokenGenerator jwtTokenGenerator,
    AppDbContext context) : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
    private readonly IJwtTokenGenerator _jwtTokenGenerator = jwtTokenGenerator;
    private readonly AppDbContext _context = context;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // 1. Email yoxlanışı
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
            throw new Exception("User with this email already exists.");

        // 2. İstifadəçi yaradılması
        var user = new ApplicationUser
        {
            Email = request.Email,
            UserName = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            EmailConfirmed = true // Sadəlik üçün birbaşa təsdiq edirik
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new Exception($"Registration failed: {errors}");
        }

        // 3. Rol təyini (Default: Customer)
        await _userManager.AddToRoleAsync(user, "Customer");

        // 4. Avtomatik Login (Token qaytarırıq)
        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
            throw new AuthenticationException("Invalid credentials.");

        // Şifrə yoxlanışı
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
        if (!result.Succeeded)
            throw new AuthenticationException("Invalid credentials.");

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
            .FirstOrDefaultAsync(x => x.Token == request.RefreshToken && x.UserId == user.Id);

        if (storedRefreshToken == null || !storedRefreshToken.IsActive)
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
}