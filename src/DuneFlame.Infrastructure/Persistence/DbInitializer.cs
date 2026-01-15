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

            logger.LogInformation("Database initialization completed successfully.");
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
        string[] roles = { "Admin", "Customer" };
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
            new Origin { Name = "Ethiopia" },
            new Origin { Name = "Colombia" },
            new Origin { Name = "Brazil" },
            new Origin { Name = "Kenya" },
            new Origin { Name = "Costa Rica" }
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
            new Category { Name = "Coffee Beans", Slug = "coffee-beans" },
            new Category { Name = "Coffee Machines", Slug = "coffee-machines" },
            new Category { Name = "Coffee Accessories", Slug = "coffee-accessories" },
            new Category { Name = "Cups & Mugs", Slug = "cups-and-mugs" },
            new Category { Name = "Coffee Filters", Slug = "coffee-filters" }
        };

        await context.Categories.AddRangeAsync(categories);
        await context.SaveChangesAsync();
    }

    private static async Task SeedProductsAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        if (await context.Products.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding Products...");

        var categories = await context.Categories.ToListAsync();
        var origins = await context.Origins.ToListAsync();

        if (!categories.Any())
        {
            logger.LogWarning("No categories found. Skipping product seeding.");
            return;
        }

        var ethiopiaOrigin = origins.FirstOrDefault(o => o.Name == "Ethiopia");
        var colombiaOrigin = origins.FirstOrDefault(o => o.Name == "Colombia");
        var brazilOrigin = origins.FirstOrDefault(o => o.Name == "Brazil");
        var kenyaOrigin = origins.FirstOrDefault(o => o.Name == "Kenya");

        var products = new List<Product>
        {
            // Coffee Beans - with origins and roast levels
            new Product
            {
                Name = "Ethiopia Yirgacheffe",
                Description = "Highest quality Ethiopian coffee with fruity aromas. This coffee is distinguished by floral and berry notes with a subtle acidity.",
                Price = 30.00m,
                DiscountPercentage = 10m,
                StockQuantity = 100,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-beans")?.Id ?? categories[0].Id,
                OriginId = ethiopiaOrigin?.Id,
                RoastLevel = RoastLevel.Light,
                Weight = 250,
                FlavorNotes = "Floral, Berry, Citrus"
            },
            new Product
            {
                Name = "Colombia Geisha",
                Description = "Premium coffee cultivated in the mountainous regions of Panama. Made from authentic Geisha beans, this is among the world's most valuable coffee varieties.",
                Price = 35.50m,
                DiscountPercentage = 0m,
                StockQuantity = 75,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-beans")?.Id ?? categories[0].Id,
                OriginId = colombiaOrigin?.Id,
                RoastLevel = RoastLevel.Light,
                Weight = 250,
                FlavorNotes = "Floral, Jasmine, Citrus"
            },
            new Product
            {
                Name = "Brazil Santos",
                Description = "Smooth and rich-flavored classic Brazilian coffee. Known for deep chocolate and dry fruit notes.",
                Price = 22.99m,
                DiscountPercentage = 15m,
                StockQuantity = 150,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-beans")?.Id ?? categories[0].Id,
                OriginId = brazilOrigin?.Id,
                RoastLevel = RoastLevel.Medium,
                Weight = 500,
                FlavorNotes = "Nutty, Chocolate, Dry Fruit"
            },
            new Product
            {
                Name = "Kenya AA",
                Description = "High-quality coffee grown in the foothills of Mount Kenya. Distinguished by dark berry notes.",
                Price = 28.75m,
                DiscountPercentage = 5m,
                StockQuantity = 85,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-beans")?.Id ?? categories[0].Id,
                OriginId = kenyaOrigin?.Id,
                RoastLevel = RoastLevel.Medium,
                Weight = 250,
                FlavorNotes = "Blackcurrant, Wine, Berry"
            },

            // Coffee Machines
            new Product
            {
                Name = "Espresso Machine Pro 3000",
                Description = "State-of-the-art espresso machine for professional-level coffee preparation. Features fully automatic system, steam system, and manual group head.",
                Price = 699.99m,
                DiscountPercentage = 15m,
                StockQuantity = 20,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-machines")?.Id ?? categories[1].Id,
                RoastLevel = RoastLevel.None,
                Weight = 5000,
                FlavorNotes = "Professional Grade"
            },
            new Product
            {
                Name = "Home Coffee Machine",
                Description = "Perfect for home use, easy to operate coffee machine. Compact design, energy efficient and durable.",
                Price = 199.99m,
                DiscountPercentage = 0m,
                StockQuantity = 45,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-machines")?.Id ?? categories[1].Id,
                RoastLevel = RoastLevel.None,
                Weight = 3000,
                FlavorNotes = "Compact, User-friendly"
            },
            new Product
            {
                Name = "French Press",
                Description = "Classic French-style coffee brewing apparatus. Coffee brewed with this immersion method yields full aroma and taste.",
                Price = 59.99m,
                DiscountPercentage = 20m,
                StockQuantity = 120,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-machines")?.Id ?? categories[1].Id,
                RoastLevel = RoastLevel.None,
                Weight = 800,
                FlavorNotes = "Classic, Traditional"
            },

            // Accessories
            new Product
            {
                Name = "Coffee Scale",
                Description = "Perfect scale for precise coffee measurement. Precision to 0.1 gram, digital display.",
                Price = 89.99m,
                DiscountPercentage = 0m,
                StockQuantity = 60,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-accessories")?.Id ?? categories[2].Id,
                RoastLevel = RoastLevel.None,
                Weight = 400,
                FlavorNotes = "Precision, Digital Display"
            },
            new Product
            {
                Name = "Coffee Grinder",
                Description = "Professional coffee grinder for grinding your own beans. Burr grinder system, 15-step grind adjustments.",
                Price = 179.99m,
                DiscountPercentage = 10m,
                StockQuantity = 40,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-accessories")?.Id ?? categories[2].Id,
                RoastLevel = RoastLevel.None,
                Weight = 1500,
                FlavorNotes = "Professional, Burr Grinder"
            },
            new Product
            {
                Name = "Coffee Tamper",
                Description = "High-quality coffee tamper. Made from stainless steel with ergonomic grip.",
                Price = 34.99m,
                DiscountPercentage = 0m,
                StockQuantity = 200,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-accessories")?.Id ?? categories[2].Id,
                RoastLevel = RoastLevel.None,
                Weight = 200,
                FlavorNotes = "Stainless Steel"
            },

            // Cups & Mugs
            new Product
            {
                Name = "Ceramic Espresso Cup",
                Description = "Handcrafted ceramic espresso cup set of 6. Heat-retaining ceramic material, antique design.",
                Price = 55.99m,
                DiscountPercentage = 18m,
                StockQuantity = 80,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "cups-and-mugs")?.Id ?? categories[3].Id,
                RoastLevel = RoastLevel.None,
                Weight = 600,
                FlavorNotes = "Ceramic, Handcrafted"
            },
            new Product
            {
                Name = "Glass Coffee Cup",
                Description = "Premium glass coffee cup set of 2. Borosilicate glass, heat-resistant and transparent.",
                Price = 29.99m,
                DiscountPercentage = 0m,
                StockQuantity = 110,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "cups-and-mugs")?.Id ?? categories[3].Id,
                RoastLevel = RoastLevel.None,
                Weight = 400,
                FlavorNotes = "Glass, Heat-resistant"
            },

            // Filters
            new Product
            {
                Name = "Paper Coffee Filter",
                Description = "Paper filters for home coffee machines. Natural paper, environmentally friendly, 100 count package.",
                Price = 12.99m,
                DiscountPercentage = 0m,
                StockQuantity = 500,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-filters")?.Id ?? categories[4].Id,
                RoastLevel = RoastLevel.None,
                Weight = 50,
                FlavorNotes = "Paper, Eco-friendly"
            },
            new Product
            {
                Name = "Metal Coffee Filter",
                Description = "Reusable metal coffee filter. Made from stainless steel, durable and eco-friendly.",
                Price = 24.99m,
                DiscountPercentage = 20m,
                StockQuantity = 200,
                IsActive = true,
                CategoryId = categories.FirstOrDefault(c => c.Slug == "coffee-filters")?.Id ?? categories[4].Id,
                RoastLevel = RoastLevel.None,
                Weight = 100,
                FlavorNotes = "Stainless Steel, Reusable"
            }
        };

        await context.Products.AddRangeAsync(products);
        await context.SaveChangesAsync();
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
            new Slider
            {
                Title = "Fresh Coffee Beans",
                Subtitle = "The finest coffee beans from around the world delivered directly to you",
                ImageUrl = "https://images.unsplash.com/photo-1559056199-641a0ac8b3f7?w=1200&q=80",
                TargetUrl = "/products?category=coffee-beans",
                Order = 1,
                IsActive = true
            },
            new Slider
            {
                Title = "Professional Coffee Machines",
                Subtitle = "Brew barista-quality coffee in your own home",
                ImageUrl = "https://images.unsplash.com/photo-1517668808822-9ebb02ae2a0e?w=1200&q=80",
                TargetUrl = "/products?category=coffee-machines",
                Order = 2,
                IsActive = true
            },
            new Slider
            {
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
            new AboutSection
            {
                Title = "Our Story",
                Content = "DuneFlame is a coffee brand established in 2020. We carefully source beans from the world's finest coffee roasters and deliver them directly to our customers. Every cup of coffee tells a story - one of quality, passion, and craftsmanship.",
                ImageUrl = "https://images.unsplash.com/photo-1495474472624-4c6730f399d4?w=600&q=80"
            },
            new AboutSection
            {
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
            return;
        }

        logger.LogInformation("Seeding Mock Orders...");

        var demoUser = await userManager.FindByEmailAsync("demo@duneflame.com");
        if (demoUser == null)
        {
            logger.LogWarning("Demo user not found. Skipping order seeding.");
            return;
        }

        var products = await context.Products.ToListAsync();
        if (!products.Any())
        {
            logger.LogWarning("No products found. Skipping order seeding.");
            return;
        }

        var orders = new List<Order>();

        // Order 1: Delivered
        var order1 = new Order
        {
            UserId = demoUser.Id,
            Status = OrderStatus.Delivered,
            TotalAmount = 0,
            ShippingAddress = "123 Main Street, New York, NY 10001, USA",
            PointsEarned = 45,
            CreatedAt = DateTime.UtcNow.AddDays(-30)
        };

        var product1 = products.FirstOrDefault(p => p.Name == "Ethiopia Yirgacheffe");
        if (product1 != null)
        {
            var sellingPrice1 = product1.Price * (1 - product1.DiscountPercentage / 100);
            order1.Items.Add(new OrderItem
            {
                ProductId = product1.Id,
                ProductName = product1.Name,
                UnitPrice = sellingPrice1,
                Quantity = 2
            });
            order1.TotalAmount += sellingPrice1 * 2;
        }

        var product2 = products.FirstOrDefault(p => p.Name == "French Press");
        if (product2 != null)
        {
            var sellingPrice2 = product2.Price * (1 - product2.DiscountPercentage / 100);
            order1.Items.Add(new OrderItem
            {
                ProductId = product2.Id,
                ProductName = product2.Name,
                UnitPrice = sellingPrice2,
                Quantity = 1
            });
            order1.TotalAmount += sellingPrice2 * 1;
        }

        orders.Add(order1);

        // Order 2: Paid
        var order2 = new Order
        {
            UserId = demoUser.Id,
            Status = OrderStatus.Paid,
            TotalAmount = 0,
            ShippingAddress = "123 Main Street, New York, NY 10001, USA",
            PointsEarned = 35,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };

        var product3 = products.FirstOrDefault(p => p.Name == "Coffee Grinder");
        if (product3 != null)
        {
            var sellingPrice3 = product3.Price * (1 - product3.DiscountPercentage / 100);
            order2.Items.Add(new OrderItem
            {
                ProductId = product3.Id,
                ProductName = product3.Name,
                UnitPrice = sellingPrice3,
                Quantity = 1
            });
            order2.TotalAmount += sellingPrice3 * 1;
        }

        orders.Add(order2);

        // Order 3: Cancelled
        var order3 = new Order
        {
            UserId = demoUser.Id,
            Status = OrderStatus.Cancelled,
            TotalAmount = 0,
            ShippingAddress = "123 Main Street, New York, NY 10001, USA",
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };

        var product4 = products.FirstOrDefault(p => p.Name == "Ceramic Espresso Cup");
        if (product4 != null)
        {
            var sellingPrice4 = product4.Price * (1 - product4.DiscountPercentage / 100);
            order3.Items.Add(new OrderItem
            {
                ProductId = product4.Id,
                ProductName = product4.Name,
                UnitPrice = sellingPrice4,
                Quantity = 2
            });
            order3.TotalAmount += sellingPrice4 * 2;
        }

        orders.Add(order3);

        // Order 4: Delivered
        var order4 = new Order
        {
            UserId = demoUser.Id,
            Status = OrderStatus.Delivered,
            TotalAmount = 0,
            ShippingAddress = "123 Main Street, New York, NY 10001, USA",
            PointsEarned = 18,
            CreatedAt = DateTime.UtcNow.AddDays(-60)
        };

        var product5 = products.FirstOrDefault(p => p.Name == "Paper Coffee Filter");
        if (product5 != null)
        {
            var sellingPrice5 = product5.Price * (1 - product5.DiscountPercentage / 100);
            order4.Items.Add(new OrderItem
            {
                ProductId = product5.Id,
                ProductName = product5.Name,
                UnitPrice = sellingPrice5,
                Quantity = 3
            });
            order4.TotalAmount += sellingPrice5 * 3;
        }

        orders.Add(order4);

        // Order 5: Delivered
        var order5 = new Order
        {
            UserId = demoUser.Id,
            Status = OrderStatus.Delivered,
            TotalAmount = 0,
            ShippingAddress = "123 Main Street, New York, NY 10001, USA",
            PointsEarned = 100,
            CreatedAt = DateTime.UtcNow.AddDays(-45)
        };

        var product6 = products.FirstOrDefault(p => p.Name == "Espresso Machine Pro 3000");
        if (product6 != null)
        {
            var sellingPrice6 = product6.Price * (1 - product6.DiscountPercentage / 100);
            order5.Items.Add(new OrderItem
            {
                ProductId = product6.Id,
                ProductName = product6.Name,
                UnitPrice = sellingPrice6,
                Quantity = 1
            });
            order5.TotalAmount += sellingPrice6 * 1;
        }

        orders.Add(order5);

        await context.Orders.AddRangeAsync(orders);
        await context.SaveChangesAsync();
    }

    private static async Task SeedMarketingDataAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        logger.LogInformation("Seeding Marketing Data...");

        // Seed Newsletter Subscriptions
        if (!await context.NewsletterSubscriptions.AnyAsync())
        {
            var newsletters = new List<NewsletterSubscription>
            {
                new NewsletterSubscription
                {
                    Email = "john@example.com",
                    IsVerified = true,
                    Source = "Footer",
                    CreatedAt = DateTime.UtcNow.AddDays(-30)
                },
                new NewsletterSubscription
                {
                    Email = "sarah@example.com",
                    IsVerified = true,
                    Source = "Popup",
                    CreatedAt = DateTime.UtcNow.AddDays(-20)
                },
                new NewsletterSubscription
                {
                    Email = "mike@example.com",
                    IsVerified = true,
                    Source = "Checkout",
                    CreatedAt = DateTime.UtcNow.AddDays(-15)
                },
                new NewsletterSubscription
                {
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
                new ContactMessage
                {
                    Name = "James Smith",
                    Email = "james@example.com",
                    Subject = "Coffee Quality Inquiry",
                    Message = "I would like more information about your Ethiopia Yirgacheffe coffee. How long can I store this coffee after opening?",
                    IsRead = true,
                    IpAddress = "192.168.1.1",
                    CreatedAt = DateTime.UtcNow.AddDays(-15)
                },
                new ContactMessage
                {
                    Name = "Sarah Johnson",
                    Email = "sarah@example.com",
                    Subject = "Bulk Corporate Order",
                    Message = "We are interested in placing a large bulk order of coffee beans and machines for our corporate office. Do you offer wholesale discounts?",
                    IsRead = true,
                    AdminReply = "Thank you for contacting us. Our corporate sales team will reach out to you shortly with a customized quote.",
                    IpAddress = "192.168.1.2",
                    CreatedAt = DateTime.UtcNow.AddDays(-8)
                },
                new ContactMessage
                {
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
}
