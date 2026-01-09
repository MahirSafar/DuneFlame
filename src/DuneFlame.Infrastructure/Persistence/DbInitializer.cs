using DuneFlame.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        try
        {
            await context.Database.MigrateAsync();

            // 1. Rolları yarat (Admin və Customer)
            string[] roles = { "Admin", "Customer" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                }
            }

            // 2. Admin istifadəçisini yarat
            var adminEmail = "admin@duneflame.com";
            if (await userManager.FindByEmailAsync(adminEmail) == null)
            {
                logger.LogInformation("Seeding Admin User...");
                var adminUser = new ApplicationUser
                {
                    UserName = adminEmail, // Identity-də UserName mütləqdir
                    Email = adminEmail,
                    FirstName = "Mahir",
                    LastName = "Admin",
                    EmailConfirmed = true
                };

                // CreateAsync şifrəni avtomatik hash-ləyir
                var result = await userManager.CreateAsync(adminUser, "Admin123!");

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }

            // 3. Settings
            if (!await context.AppSettings.AnyAsync())
            {
                await context.AppSettings.AddAsync(new AppSetting { Key = "RewardPercentage", Value = "5" });
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing the database.");
            throw;
        }
    }
}