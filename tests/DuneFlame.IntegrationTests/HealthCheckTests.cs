using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Text.Json;

namespace DuneFlame.IntegrationTests;

public class HealthCheckTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_HealthEndpoint_Returns200OK()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        // Verify the response structure
        root.TryGetProperty("status", out var statusElement).Should().BeTrue();
        statusElement.GetString().Should().NotBeNullOrEmpty();

        // The status can be "Healthy" or "Degraded" depending on Redis/DB availability
        var status = statusElement.GetString();
        status.Should().BeOneOf("Healthy", "Degraded", "Unhealthy");

        // Verify checks array exists
        root.TryGetProperty("checks", out var checksElement).Should().BeTrue();
        checksElement.ValueKind.Should().Be(JsonValueKind.Array);
    }
}
