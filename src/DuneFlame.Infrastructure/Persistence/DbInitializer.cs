using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
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
            // Apply migrations or ensure database is created
            if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                await context.Database.EnsureCreatedAsync();
            }
            else
            {
                await context.Database.MigrateAsync();
            }

            // 1. Seed Roles
            await SeedRolesAsync(roleManager, logger);

            // 2. Seed Users
            await SeedUsersAsync(userManager, logger);

            // 3. Seed Settings
            await SeedSettingsAsync(context, logger);

            // 4. Seed Origins
            await SeedOriginsAsync(context, logger);

            // 5. Seed Categories
            await SeedCategoriesAsync(context, logger);

            // 6. Seed Products
            await SeedProductsAsync(context, logger);

            // 7. Seed CMS Content (Sliders & About)
            await SeedCmsContentAsync(context, logger);

            // 8. Seed Mock Orders
            await SeedOrdersAsync(context, userManager, logger);

            // 9. Seed Marketing Data
            await SeedMarketingDataAsync(context, logger);

            // 10. Seed Shipping Data (Countries, Cities, Rates)
            await SeedShippingDataAsync(context, logger);

            logger.LogInformation("Database initialization completed successfully.");
            logger.LogInformation("✓ Multi-Currency Support: ProductPrices seeded for USD, AED");
            logger.LogInformation("✓ Test Orders: Demo orders created with multi-currency support (USD, AED)");
            logger.LogInformation("✓ Shipping System: Countries, cities, and rates seeded for database-driven shipping");
            logger.LogInformation("✓ HybridCache: First product load will warm L1 (memory) and L2 (Redis) caches");
            }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing the database.");
            throw;
        }
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roleManager, ILogger<AppDbContext> logger)
    {
        logger.LogInformation("Seeding Roles...");
        string[] roles = ["Admin", "Customer"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }
    }

    private static async Task SeedUsersAsync(UserManager<ApplicationUser> userManager, ILogger<AppDbContext> logger)
    {
        logger.LogInformation("Seeding Users...");

        // Admin user
        var adminEmail = "admin@duneflame.com";
        if (await userManager.FindByEmailAsync(adminEmail) == null)
        {
            var adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "Mahir",
                LastName = "Admin",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(adminUser, "Admin123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }

        // Demo customer user
        var demoEmail = "demo@duneflame.com";
        if (await userManager.FindByEmailAsync(demoEmail) == null)
        {
            var demoUser = new ApplicationUser
            {
                UserName = demoEmail,
                Email = demoEmail,
                FirstName = "John",
                LastName = "Doe",
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(demoUser, "Customer123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(demoUser, "Customer");
            }
        }
    }

    private static async Task SeedSettingsAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        if (await context.AppSettings.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding Settings...");
        await context.AppSettings.AddAsync(new AppSetting { Key = "RewardPercentage", Value = "5" });
        await context.SaveChangesAsync();
    }

    private static async Task SeedOriginsAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        if (await context.Origins.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding Origins...");
        var origins = new List<Origin>
        {
            new() { Name = "Ethiopia" },
            new() { Name = "Colombia" },
            new() { Name = "Brazil" },
            new() { Name = "Kenya" },
            new() { Name = "Costa Rica" }
        };

        await context.Origins.AddRangeAsync(origins);
        await context.SaveChangesAsync();
    }

    private static async Task SeedCategoriesAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        if (await context.Categories.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding Categories...");
        var categories = new List<Category>
        {
            new() { Name = "Coffee Beans", Slug = "coffee-beans" },
            new() { Name = "Coffee Machines", Slug = "coffee-machines" },
            new() { Name = "Cups & Mugs", Slug = "cups-and-mugs" },
            new() { Name = "Coffee Accessories", Slug = "coffee-accessories" },
            new() { Name = "Coffee Filters", Slug = "coffee-filters" }
        };

        await context.Categories.AddRangeAsync(categories);
        await context.SaveChangesAsync();
    }

    private static async Task SeedProductsAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        // Check if weights already seeded (idempotent)
        if (await context.ProductWeights.AnyAsync())
        {
            logger.LogInformation("Products already seeded. Skipping...");
            return;
        }

        logger.LogInformation("Seeding ProductWeights, RoastLevels, GrindTypes, and Products for Silo v2...");

        // 1. Seed ProductWeights (Master Data)
        var weights = new List<ProductWeight>
        {
            new() { Label = "250g", Grams = 250 },
            new() { Label = "1kg", Grams = 1000 }
        };
        await context.ProductWeights.AddRangeAsync(weights);
        await context.SaveChangesAsync();

        // Reload to get IDs
        var weight250g = await context.ProductWeights.FirstOrDefaultAsync(w => w.Grams == 250);
        var weight1kg = await context.ProductWeights.FirstOrDefaultAsync(w => w.Grams == 1000);

        // 2. Seed RoastLevels (Master Data)
        var roastLevels = new List<RoastLevelEntity>
        {
            new() { Name = "Light" },
            new() { Name = "Medium" },
            new() { Name = "Dark" }
        };
        await context.RoastLevels.AddRangeAsync(roastLevels);
        await context.SaveChangesAsync();

        // Reload to get IDs
        var roastLight = await context.RoastLevels.FirstOrDefaultAsync(r => r.Name == "Light");
        var roastMedium = await context.RoastLevels.FirstOrDefaultAsync(r => r.Name == "Medium");
        var roastDark = await context.RoastLevels.FirstOrDefaultAsync(r => r.Name == "Dark");

        // 3. Seed GrindTypes (Master Data)
        var grindTypes = new List<GrindType>
        {
            new() { Name = "Whole Bean" },
            new() { Name = "Espresso" },
            new() { Name = "Filter" },
            new() { Name = "French Press" }
        };
        await context.GrindTypes.AddRangeAsync(grindTypes);
        await context.SaveChangesAsync();

        // Reload to get IDs
        var grindWholeBean = await context.GrindTypes.FirstOrDefaultAsync(g => g.Name == "Whole Bean");
        var grindEspresso = await context.GrindTypes.FirstOrDefaultAsync(g => g.Name == "Espresso");
        var grindFilter = await context.GrindTypes.FirstOrDefaultAsync(g => g.Name == "Filter");
        var grindFrenchPress = await context.GrindTypes.FirstOrDefaultAsync(g => g.Name == "French Press");

        // 4. Get categories and origins
        var categories = await context.Categories.ToListAsync();
        var origins = await context.Origins.ToListAsync();

        if (categories.Count == 0 || origins.Count == 0)
        {
            logger.LogWarning("Categories or Origins not found. Skipping product seeding.");
            return;
        }

        var coffeeBeansCategory = categories.FirstOrDefault(c => c.Slug == "coffee-beans");
        var ethiopiaOrigin = origins.FirstOrDefault(o => o.Name == "Ethiopia");

        if (coffeeBeansCategory == null || ethiopiaOrigin == null)
        {
            logger.LogWarning("Coffee Beans category or Ethiopia origin not found.");
            return;
        }

        // 5. Create Sample Product: Ethiopian Yirgacheffe
        var ethiopianProduct = new Product
        {
            Name = "Ethiopian Yirgacheffe",
            Slug = "ethiopian-yirgacheffe",
            Description = "Highest quality Ethiopian coffee with fruity aromas. This coffee is distinguished by floral and berry notes with a subtle acidity.",
            StockInKg = 50.0m, // Central Silo Stock
            IsActive = true,
            CategoryId = coffeeBeansCategory.Id,
            OriginId = ethiopiaOrigin.Id
        };

        // Add M2M relationships for RoastLevels
        if (roastMedium != null) ethiopianProduct.RoastLevels.Add(roastMedium);
        if (roastDark != null) ethiopianProduct.RoastLevels.Add(roastDark);

        // Add M2M relationships for GrindTypes
        if (grindWholeBean != null) ethiopianProduct.GrindTypes.Add(grindWholeBean);
        if (grindEspresso != null) ethiopianProduct.GrindTypes.Add(grindEspresso);

        await context.Products.AddAsync(ethiopianProduct);
        await context.SaveChangesAsync();

        // 6. Create ProductPrices for Ethiopian Yirgacheffe (Multi-Currency Support)
        var prices = new List<ProductPrice>();

        // USD Prices
        if (weight250g != null)
        {
            prices.Add(new ProductPrice
            {
                ProductId = ethiopianProduct.Id,
                ProductWeightId = weight250g.Id,
                Price = 15.00m,
                CurrencyCode = Currency.USD
            });
        }

        if (weight1kg != null)
        {
            prices.Add(new ProductPrice
            {
                ProductId = ethiopianProduct.Id,
                ProductWeightId = weight1kg.Id,
                Price = 55.00m,
                CurrencyCode = Currency.USD
            });
        }

        // AED Prices (approximately 3.67x USD rate)
        if (weight250g != null)
        {
            prices.Add(new ProductPrice
            {
                ProductId = ethiopianProduct.Id,
                ProductWeightId = weight250g.Id,
                Price = 55.05m,
                CurrencyCode = Currency.AED
            });
        }

        if (weight1kg != null)
        {
            prices.Add(new ProductPrice
            {
                ProductId = ethiopianProduct.Id,
                ProductWeightId = weight1kg.Id,
                Price = 201.85m,
                CurrencyCode = Currency.AED
            });
        }

        await context.ProductPrices.AddRangeAsync(prices);
        await context.SaveChangesAsync();

        logger.LogInformation("Silo v2 product seeding completed successfully with multi-currency support.");
        logger.LogInformation("Created ProductPrices for: USD, AED (2 currencies x 2 weights = 4 price records).");
    }

    private static async Task SeedCmsContentAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        if (await context.Sliders.AnyAsync() || await context.AboutSections.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding CMS Content...");

        // Seed Sliders
        var sliders = new List<Slider>
        {
            new() {
                Title = "Fresh Coffee Beans",
                Subtitle = "The finest coffee beans from around the world delivered directly to you",
                ImageUrl = "https://images.unsplash.com/photo-1559056199-641a0ac8b3f7?w=1200&q=80",
                TargetUrl = "/products?category=coffee-beans",
                Order = 1,
                IsActive = true
            },
            new() {
                Title = "Professional Coffee Machines",
                Subtitle = "Brew barista-quality coffee in your own home",
                ImageUrl = "https://images.unsplash.com/photo-1517668808822-9ebb02ae2a0e?w=1200&q=80",
                TargetUrl = "/products?category=coffee-machines",
                Order = 2,
                IsActive = true
            },
            new() {
                Title = "Accessories & Filters",
                Subtitle = "Enhance your coffee brewing experience",
                ImageUrl = "https://images.unsplash.com/photo-1559056199-641a0ac8b3f7?w=1200&q=80",
                TargetUrl = "/products?category=coffee-accessories",
                Order = 3,
                IsActive = true
            }
        };

        await context.Sliders.AddRangeAsync(sliders);

        // Seed About Sections
        var aboutSections = new List<AboutSection>
        {
            new() {
                Title = "Our Story",
                Content = "DuneFlame is a coffee brand established in 2020. We carefully source beans from the world's finest coffee roasters and deliver them directly to our customers. Every cup of coffee tells a story - one of quality, passion, and craftsmanship.",
                ImageUrl = "https://images.unsplash.com/photo-1495474472624-4c6730f399d4?w=600&q=80"
            },
            new() {
                Title = "Sustainability Commitment",
                Content = "We are committed to the environment and coffee farming communities. All our coffee beans are certified fair trade and grown using sustainable practices. Every purchase supports farmers, forest conservation efforts, and social initiatives.",
                ImageUrl = "https://images.unsplash.com/photo-1447933601403-0c6688e7566e?w=600&q=80"
            }
        };

        await context.AboutSections.AddRangeAsync(aboutSections);
        await context.SaveChangesAsync();
    }

    private static async Task SeedOrdersAsync(AppDbContext context, UserManager<ApplicationUser> userManager, ILogger<AppDbContext> logger)
    {
        if (await context.Orders.AnyAsync())
        {
            logger.LogInformation("Orders already seeded. Skipping...");
            return;
        }

        logger.LogInformation("Seeding Test Orders with Multi-Currency Support...");

        // Get demo user
        var demoUser = await userManager.FindByEmailAsync("demo@duneflame.com");
        if (demoUser == null)
        {
            logger.LogWarning("Demo user not found. Skipping order seeding.");
            return;
        }

        // Get product price for USD
        var productPrice = await context.ProductPrices
            .Include(pp => pp.Weight)
            .FirstOrDefaultAsync(pp => pp.CurrencyCode == Currency.USD && pp.Weight!.Grams == 250);

        if (productPrice == null)
        {
            logger.LogWarning("ProductPrice not found. Skipping order seeding.");
            return;
        }

        // Create test order in USD
        var usdOrder = new Order
        {
            UserId = demoUser.Id,
            ShippingAddress = "123 Coffee Street, Seattle, WA 98101, USA",
            Status = OrderStatus.Paid,
            TotalAmount = 15.00m,
            PointsRedeemed = 0,
            PointsEarned = 15,
            PaymentIntentId = "pi_test_usd_" + Guid.NewGuid().ToString().Substring(0, 8),
            CurrencyCode = Currency.USD,
            CreatedAt = DateTime.UtcNow.AddDays(-30)
        };

        // Add order item
        var usdOrderItem = new OrderItem
        {
            ProductPriceId = productPrice.Id,
            ProductName = "Ethiopian Yirgacheffe",
            UnitPrice = 15.00m,
            Quantity = 1,
            CurrencyCode = Currency.USD
        };
        usdOrder.Items.Add(usdOrderItem);

        await context.Orders.AddAsync(usdOrder);

        // Create test order in AED
        var aedProductPrice = await context.ProductPrices
            .Include(pp => pp.Weight)
            .FirstOrDefaultAsync(pp => pp.CurrencyCode == Currency.AED && pp.Weight!.Grams == 250);

        if (aedProductPrice != null)
        {
            var aedOrder = new Order
            {
                UserId = demoUser.Id,
                ShippingAddress = "Downtown, Dubai, UAE",
                Status = OrderStatus.Paid,
                TotalAmount = 55.05m,
                PointsRedeemed = 0,
                PointsEarned = 55,
                PaymentIntentId = "pi_test_aed_" + Guid.NewGuid().ToString().Substring(0, 8),
                CurrencyCode = Currency.AED,
                CreatedAt = DateTime.UtcNow.AddDays(-15)
            };

            var aedOrderItem = new OrderItem
            {
                ProductPriceId = aedProductPrice.Id,
                ProductName = "Ethiopian Yirgacheffe",
                UnitPrice = 55.05m,
                Quantity = 1,
                CurrencyCode = Currency.AED
            };
            aedOrder.Items.Add(aedOrderItem);

            await context.Orders.AddAsync(aedOrder);
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Test orders seeded successfully with multi-currency support.");
    }

    private static async Task SeedMarketingDataAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        logger.LogInformation("Seeding Marketing Data...");

        // Seed Newsletter Subscriptions
        if (!await context.NewsletterSubscriptions.AnyAsync())
        {
            var newsletters = new List<NewsletterSubscription>
            {
                new() {
                    Email = "john@example.com",
                    IsVerified = true,
                    Source = "Footer",
                    CreatedAt = DateTime.UtcNow.AddDays(-30)
                },
                new() {
                    Email = "sarah@example.com",
                    IsVerified = true,
                    Source = "Popup",
                    CreatedAt = DateTime.UtcNow.AddDays(-20)
                },
                new() {
                    Email = "mike@example.com",
                    IsVerified = true,
                    Source = "Checkout",
                    CreatedAt = DateTime.UtcNow.AddDays(-15)
                },
                new() {
                    Email = "emma@example.com",
                    IsVerified = false,
                    Source = "Footer",
                    CreatedAt = DateTime.UtcNow.AddDays(-5)
                }
            };

            await context.NewsletterSubscriptions.AddRangeAsync(newsletters);
        }

        // Seed Contact Messages
        if (!await context.ContactMessages.AnyAsync())
        {
            var contactMessages = new List<ContactMessage>
            {
                new() {
                    Name = "James Smith",
                    Email = "james@example.com",
                    Subject = "Coffee Quality Inquiry",
                    Message = "I would like more information about your Ethiopia Yirgacheffe coffee. How long can I store this coffee after opening?",
                    IsRead = true,
                    IpAddress = "192.168.1.1",
                    CreatedAt = DateTime.UtcNow.AddDays(-15)
                },
                new() {
                    Name = "Sarah Johnson",
                    Email = "sarah@example.com",
                    Subject = "Bulk Corporate Order",
                    Message = "We are interested in placing a large bulk order of coffee beans and machines for our corporate office. Do you offer wholesale discounts?",
                    IsRead = true,
                    AdminReply = "Thank you for contacting us. Our corporate sales team will reach out to you shortly with a customized quote.",
                    IpAddress = "192.168.1.2",
                    CreatedAt = DateTime.UtcNow.AddDays(-8)
                },
                new() {
                    Name = "Michael Brown",
                    Email = "michael@example.com",
                    Subject = "Damaged Product Received",
                    Message = "The French Press I ordered arrived broken. Can you please send a replacement?",
                    IsRead = false,
                    IpAddress = "192.168.1.3",
                    CreatedAt = DateTime.UtcNow.AddDays(-2)
                }
            };

            await context.ContactMessages.AddRangeAsync(contactMessages);
        }

                await context.SaveChangesAsync();
            }

            private static async Task SeedShippingDataAsync(AppDbContext context, ILogger<AppDbContext> logger)
            {
                logger.LogInformation("Seeding Shipping Data (Countries, Cities, Rates)...");

                // Seed Countries (GCC Members Only)
                if (!await context.Countries.AnyAsync())
                {
                    var countries = new List<Country>
                    {
                        new() { Name = "Saudi Arabia", Code = "SA", IsActive = true },
                        new() { Name = "United Arab Emirates", Code = "AE", IsActive = true },
                        new() { Name = "Qatar", Code = "QA", IsActive = true },
                        new() { Name = "Kuwait", Code = "KW", IsActive = true },
                        new() { Name = "Bahrain", Code = "BH", IsActive = true },
                        new() { Name = "Oman", Code = "OM", IsActive = true }
                    };

                    await context.Countries.AddRangeAsync(countries);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Countries seeded: {CountryCount} GCC countries added", countries.Count);
                }

                // Seed Cities (GCC Countries)
                if (!await context.Cities.AnyAsync())
                {
                    var countries = await context.Countries.ToListAsync();
                    var cities = new List<City>();

                    // Saudi Arabia Cities/Regions
                    var saCountry = countries.FirstOrDefault(c => c.Code == "SA");
                    if (saCountry != null)
                    {
                        cities.AddRange(new[]
                        {
                            new City { Name = "Riyadh", CountryId = saCountry.Id },
                            new City { Name = "Jeddah", CountryId = saCountry.Id },
                            new City { Name = "Dammam", CountryId = saCountry.Id },
                            new City { Name = "Khobar", CountryId = saCountry.Id },
                            new City { Name = "Dhahran", CountryId = saCountry.Id },
                            new City { Name = "Mecca", CountryId = saCountry.Id },
                            new City { Name = "Medina", CountryId = saCountry.Id },
                            new City { Name = "Tabuk", CountryId = saCountry.Id },
                            new City { Name = "Buraydah", CountryId = saCountry.Id },
                            new City { Name = "Hail", CountryId = saCountry.Id },
                            new City { Name = "Qassim", CountryId = saCountry.Id },
                            new City { Name = "Abha", CountryId = saCountry.Id },
                            new City { Name = "Yanbu", CountryId = saCountry.Id },
                            new City { Name = "Al Qurayyat", CountryId = saCountry.Id },
                            new City { Name = "Sakaka", CountryId = saCountry.Id }
                        });
                    }

                    // United Arab Emirates Cities/Regions (Emirates)
                    var aeCountry = countries.FirstOrDefault(c => c.Code == "AE");
                    if (aeCountry != null)
                    {
                        cities.AddRange(new[]
                        {
                            new City { Name = "Dubai", CountryId = aeCountry.Id },
                            new City { Name = "Abu Dhabi", CountryId = aeCountry.Id },
                            new City { Name = "Sharjah", CountryId = aeCountry.Id },
                            new City { Name = "Ajman", CountryId = aeCountry.Id },
                            new City { Name = "Ras Al Khaimah", CountryId = aeCountry.Id },
                            new City { Name = "Fujairah", CountryId = aeCountry.Id },
                            new City { Name = "Umm Al Quwain", CountryId = aeCountry.Id },
                            new City { Name = "Al Ain", CountryId = aeCountry.Id },
                            new City { Name = "Mussafah", CountryId = aeCountry.Id },
                            new City { Name = "Khalifa City", CountryId = aeCountry.Id },
                            new City { Name = "Deira", CountryId = aeCountry.Id },
                            new City { Name = "Bur Dubai", CountryId = aeCountry.Id },
                            new City { Name = "Jumeirah", CountryId = aeCountry.Id },
                            new City { Name = "Downtown Dubai", CountryId = aeCountry.Id }
                        });
                    }

                    // Qatar Cities/Regions
                    var qaCountry = countries.FirstOrDefault(c => c.Code == "QA");
                    if (qaCountry != null)
                    {
                        cities.AddRange(new[]
                        {
                            new City { Name = "Doha", CountryId = qaCountry.Id },
                            new City { Name = "Al Rayyan", CountryId = qaCountry.Id },
                            new City { Name = "Al Wakrah", CountryId = qaCountry.Id },
                            new City { Name = "Al Khor", CountryId = qaCountry.Id },
                            new City { Name = "Lusail", CountryId = qaCountry.Id },
                            new City { Name = "Umm Salal", CountryId = qaCountry.Id },
                            new City { Name = "Al Shamal", CountryId = qaCountry.Id },
                            new City { Name = "Al Daayen", CountryId = qaCountry.Id },
                            new City { Name = "Mesaieed", CountryId = qaCountry.Id }
                        });
                    }

                    // Kuwait Cities/Regions (Governorates)
                    var kwCountry = countries.FirstOrDefault(c => c.Code == "KW");
                    if (kwCountry != null)
                    {
                        cities.AddRange(new[]
                        {
                            new City { Name = "Kuwait City", CountryId = kwCountry.Id },
                            new City { Name = "Al Ahmadi", CountryId = kwCountry.Id },
                            new City { Name = "Al Farwaniyah", CountryId = kwCountry.Id },
                            new City { Name = "Jahra", CountryId = kwCountry.Id },
                            new City { Name = "Mubarak Al-Kabeer", CountryId = kwCountry.Id },
                            new City { Name = "Sabah Al-Salem", CountryId = kwCountry.Id },
                            new City { Name = "Hawalli", CountryId = kwCountry.Id },
                            new City { Name = "Salmiya", CountryId = kwCountry.Id },
                            new City { Name = "Abbasiya", CountryId = kwCountry.Id },
                            new City { Name = "Mahboula", CountryId = kwCountry.Id }
                        });
                    }

                    // Bahrain Cities/Regions
                    var bhCountry = countries.FirstOrDefault(c => c.Code == "BH");
                    if (bhCountry != null)
                    {
                        cities.AddRange(new[]
                        {
                            new City { Name = "Manama", CountryId = bhCountry.Id },
                            new City { Name = "Muharraq", CountryId = bhCountry.Id },
                            new City { Name = "Riffa", CountryId = bhCountry.Id },
                            new City { Name = "Isa Town", CountryId = bhCountry.Id },
                            new City { Name = "Al Khbar", CountryId = bhCountry.Id },
                            new City { Name = "Sitra", CountryId = bhCountry.Id },
                            new City { Name = "Budaiya", CountryId = bhCountry.Id },
                            new City { Name = "Juffair", CountryId = bhCountry.Id },
                            new City { Name = "Adliya", CountryId = bhCountry.Id }
                        });
                    }

                    // Oman Cities/Regions (Governorates)
                    var omCountry = countries.FirstOrDefault(c => c.Code == "OM");
                    if (omCountry != null)
                    {
                        cities.AddRange(new[]
                        {
                            new City { Name = "Muscat", CountryId = omCountry.Id },
                            new City { Name = "Seeb", CountryId = omCountry.Id },
                            new City { Name = "Salalah", CountryId = omCountry.Id },
                            new City { Name = "Nizwa", CountryId = omCountry.Id },
                            new City { Name = "Sohar", CountryId = omCountry.Id },
                            new City { Name = "Ibra", CountryId = omCountry.Id },
                            new City { Name = "Sur", CountryId = omCountry.Id },
                            new City { Name = "Barka", CountryId = omCountry.Id },
                            new City { Name = "Saham", CountryId = omCountry.Id },
                            new City { Name = "Qurayyat", CountryId = omCountry.Id },
                            new City { Name = "Mirbat", CountryId = omCountry.Id },
                            new City { Name = "Adam", CountryId = omCountry.Id }
                        });
                    }

                    await context.Cities.AddRangeAsync(cities);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Cities seeded: {CityCount} GCC cities added", cities.Count);
                }

                    // Seed Shipping Rates
                if (!await context.ShippingRates.AnyAsync())
                {
                    var countries = await context.Countries.ToListAsync();
                    var rates = new List<ShippingRate>();

                    foreach (var country in countries)
                    {
                        // Add rates for USD and AED
                        rates.Add(new ShippingRate { CountryId = country.Id, Currency = Currency.USD, Cost = GetShippingCostUSD(country.Code) });
                        rates.Add(new ShippingRate { CountryId = country.Id, Currency = Currency.AED, Cost = GetShippingCostAED(country.Code) });
                    }

                    await context.ShippingRates.AddRangeAsync(rates);
                    await context.SaveChangesAsync();
                    logger.LogInformation("Shipping Rates seeded: {RateCount} rates added", rates.Count);
                }

                logger.LogInformation("Shipping data initialization completed successfully.");
            }

            private static decimal GetShippingCostUSD(string countryCode)
            {
                return countryCode switch
                {
                    "SA" => 18.99m,
                    "AE" => 22.99m,
                    "QA" => 21.99m,
                    "KW" => 20.99m,
                    "BH" => 19.99m,
                    "OM" => 24.99m,
                    _ => 0m
                };
            }

            private static decimal GetShippingCostAED(string countryCode)
            {
                return countryCode switch
                {
                    "SA" => 69.99m,
                    "AE" => 15.00m,
                    "QA" => 80.99m,
                    "KW" => 77.00m,
                    "BH" => 73.00m,
                    "OM" => 91.99m,
                    _ => 0m
                };
            }
        }
