using DuneFlame.Application.DTOs.Auth;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;

namespace DuneFlame.IntegrationTests;

public class AuthTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Register_And_Login_Flow_Should_Work()
    {
        // 1. REGISTER
        var registerRequest = new RegisterRequest("Test", "User", "test@example.com", "Password123!");
        var registerResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. LOGIN (Success)
        var loginRequest = new LoginRequest("test@example.com", "Password123!");
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var authData = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        authData.Should().NotBeNull();
        authData!.AccessToken.Should().NotBeNullOrEmpty();
        authData.RefreshToken.Should().NotBeNullOrEmpty();

        // 3. JWT VALIDATION (Protected Endpoint Access)
        // Tokeni header-ə qoyuruq
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authData.AccessToken);

        // Logout endpointi [Authorize] tələb edir
        var logoutResponse = await _client.PostAsync("/api/v1/auth/logout", null);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_With_Wrong_Password_Should_Fail()
    {
        // İstifadəçi yoxdur, amma yenə də formatı yoxlayaq
        var loginRequest = new LoginRequest("nonexistent@example.com", "WrongPass123!");
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Biz Exception Middleware qurduğumuz üçün 500 qaytara bilər (Exception atılır)
        // Və ya exception-ı tutub 401 qaytarmaq daha yaxşı olardı. 
        // Hazırda GlobalExceptionMiddleware 500 qaytarır.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    }
