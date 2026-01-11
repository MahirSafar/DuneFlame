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
            // Provider adını yoxlayırıq 
            if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                await context.Database.EnsureCreatedAsync();
            }
            else
            {
                await context.Database.MigrateAsync();
            }

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

                        // 4. Categories
                        await SeedCategoriesAsync(context, logger);

                        // 5. Products
                        await SeedProductsAsync(context, logger);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "An error occurred while initializing the database.");
                        throw;
                    }
                }

                private static async Task SeedCategoriesAsync(AppDbContext context, ILogger<AppDbContext> logger)
                {
                    if (await context.Categories.AnyAsync())
                    {
                        return; // Categories already seeded
                    }

                    logger.LogInformation("Seeding Categories...");

                    var categories = new List<Category>
                    {
                        new Category { Name = "Qəhvə Dənələri", Slug = "qehve-deneleri" },
                        new Category { Name = "Qəhvə Maşınları", Slug = "qehve-masinlari" },
                        new Category { Name = "Qəhvə Aksessuarları", Slug = "qehve-aksessuarlari" },
                        new Category { Name = "Fincan və Kupalar", Slug = "fincan-va-kupalar" },
                        new Category { Name = "Qəhvə Filtri", Slug = "qehve-filtri" }
                    };

                    await context.Categories.AddRangeAsync(categories);
                    await context.SaveChangesAsync();
                }

                private static async Task SeedProductsAsync(AppDbContext context, ILogger<AppDbContext> logger)
                {
                    if (await context.Products.AnyAsync())
                    {
                        return; // Products already seeded
                    }

                    logger.LogInformation("Seeding Products...");

                    var categories = await context.Categories.ToListAsync();

                    if (!categories.Any())
                    {
                        logger.LogWarning("No categories found. Skipping product seeding.");
                        return;
                    }

                    var products = new List<Product>
                    {
                        // Qəhvə Dənələri kategoriyas
                        new Product
                        {
                            Name = "Etiyopiya Yirgacheffe",
                            Description = "Ən yüksək keyfiyyətli Etiyopiya qəhvəsi, meyvə aromalarına malikdir",
                            Price = 25.99m,
                            OldPrice = 29.99m,
                            StockQuantity = 100,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-deneleri")?.Id ?? categories[0].Id
                        },
                        new Product
                        {
                            Name = "Kolumbiya Geisha",
                            Description = "Panamamanın dağlı bölgəsində yetişən seçilmiş qəhvə",
                            Price = 35.50m,
                            OldPrice = null,
                            StockQuantity = 75,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-deneleri")?.Id ?? categories[0].Id
                        },
                        new Product
                        {
                            Name = "Braziliya Santos",
                            Description = "Yumsaq, zəngin ləzzvəti olan klassik Braziliya qəhvəsi",
                            Price = 18.99m,
                            OldPrice = 22.99m,
                            StockQuantity = 150,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-deneleri")?.Id ?? categories[0].Id
                        },
                        new Product
                        {
                            Name = "Kenyada AA",
                            Description = "Napoleon dağlarının ətəyində yetişən yüksək keyfiyyətli qəhvə",
                            Price = 28.75m,
                            OldPrice = null,
                            StockQuantity = 85,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-deneleri")?.Id ?? categories[0].Id
                        },

                        // Qəhvə Maşınları kategoriyas
                        new Product
                        {
                            Name = "Espresso Maşını Pro 3000",
                            Description = "Peşəkar səviyyədə qəhvə hazırlamaq üçün ən müasir espresso maşını",
                            Price = 599.99m,
                            OldPrice = 699.99m,
                            StockQuantity = 20,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-masinlari")?.Id ?? categories[1].Id
                        },
                        new Product
                        {
                            Name = "Ev Tipli Qəhvə Maşını",
                            Description = "Ev üçün ideal, istifadəsi asan qəhvə maşını",
                            Price = 199.99m,
                            OldPrice = null,
                            StockQuantity = 45,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-masinlari")?.Id ?? categories[1].Id
                        },
                        new Product
                        {
                            Name = "Fransa Presinə",
                            Description = "Klassik fransa tərzində qəhvə hazırlaması aparatı",
                            Price = 49.99m,
                            OldPrice = 59.99m,
                            StockQuantity = 120,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-masinlari")?.Id ?? categories[1].Id
                        },

                        // Qəhvə Aksessuarları kategoriyas
                        new Product
                        {
                            Name = "Qəhvə Tərəzisi",
                            Description = "Dəqiq ölçülü qəhvə hazırlama üçün mükəmməl tərəzi",
                            Price = 89.99m,
                            OldPrice = null,
                            StockQuantity = 60,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-aksessuarlari")?.Id ?? categories[2].Id
                        },
                        new Product
                        {
                            Name = "Qəhvə Ögütücü",
                            Description = "Öz qəhvənizi ögütmək üçün professional ögütücü",
                            Price = 149.99m,
                            OldPrice = 179.99m,
                            StockQuantity = 40,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-aksessuarlari")?.Id ?? categories[2].Id
                        },
                        new Product
                        {
                            Name = "Qəhvə Tamper",
                            Description = "Yüksək keyfiyyətli qəhvə tamperi",
                            Price = 34.99m,
                            OldPrice = null,
                            StockQuantity = 200,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-aksessuarlari")?.Id ?? categories[2].Id
                        },

                        // Fincan və Kupalar kategoriyas
                        new Product
                        {
                            Name = "Seramik Espresso Fincanı",
                            Description = "Fərdi işlənmiş seramik espresso fincalı, 6 əd",
                            Price = 45.99m,
                            OldPrice = 55.99m,
                            StockQuantity = 80,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "fincan-va-kupalar")?.Id ?? categories[3].Id
                        },
                        new Product
                        {
                            Name = "Stəkən Qəhvə Fincanı",
                            Description = "Dövlət keyfiyyətli stəkən fincan, 2 əd",
                            Price = 29.99m,
                            OldPrice = null,
                            StockQuantity = 110,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "fincan-va-kupalar")?.Id ?? categories[3].Id
                        },

                        // Qəhvə Filtri kategoriyas
                        new Product
                        {
                            Name = "Kağız Qəhvə Filtri (100 əd)",
                            Description = "Ev tipli qəhvə maşınları üçün kağız filtri",
                            Price = 12.99m,
                            OldPrice = null,
                            StockQuantity = 500,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-filtri")?.Id ?? categories[4].Id
                        },
                        new Product
                        {
                            Name = "Metal Qəhvə Filtri",
                            Description = "Yenidən istifadə edilə biləcək metal qəhvə filtri",
                            Price = 19.99m,
                            OldPrice = 24.99m,
                            StockQuantity = 200,
                            IsActive = true,
                            CategoryId = categories.FirstOrDefault(c => c.Slug == "qehve-filtri")?.Id ?? categories[4].Id
                        }
                    };

                    await context.Products.AddRangeAsync(products);
                    await context.SaveChangesAsync();
                }
            }