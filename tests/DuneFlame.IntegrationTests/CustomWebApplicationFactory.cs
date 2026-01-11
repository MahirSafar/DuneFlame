using DuneFlame.Domain.Entities;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DuneFlame.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove all DbContext related services to avoid multiple database provider registration
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                           d.ServiceType == typeof(DbContextOptions) ||
                           (d.ServiceType?.Name?.Contains("DbContext") ?? false))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
                services.Remove(descriptor);

            // Remove PostgreSQL provider services
            var postgresDescriptors = services
                .Where(d => d.ServiceType?.FullName?.Contains("Npgsql") ?? false)
                .ToList();

            foreach (var descriptor in postgresDescriptors)
                services.Remove(descriptor);

            // Remove rate limiter for testing to prevent test interference
            var rateLimiterDescriptor = services.FirstOrDefault(d => 
                d.ServiceType == typeof(RateLimiterOptions));
            if (rateLimiterDescriptor != null)
            {
                services.Remove(rateLimiterDescriptor);
            }

            // Register InMemory DbContext instead with fresh service collection
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase("InMemoryDbForTesting");
                options.ConfigureWarnings(w => 
                    w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
            });
        });

        // Skip running DbInitializer for testing
        builder.UseEnvironment("Testing");
    }

    protected override async void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);

        // Initialize the InMemory database with necessary schema and seed data
        using (var scope = Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // EnsureCreatedAsync creates the database schema for InMemory provider
            await context.Database.EnsureCreatedAsync();

            // Seed required roles
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var roles = new[] { "Customer", "Admin", "User" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                }
            }
        }
    }
}
