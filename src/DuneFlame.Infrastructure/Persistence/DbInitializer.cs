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
            new() { Name = "Costa Rica" },
            new() { Name = "Malaysia" }
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

        logger.LogInformation("Seeding Categories with translations...");
        var categories = new List<Category>
        {
            new()
            {
                Slug = "coffee-beans",
                Translations = new List<CategoryTranslation>
                {
                    new() { LanguageCode = "en", Name = "Coffee Beans" },
                    new() { LanguageCode = "ar", Name = "حبوب القهوة" }
                }
            },
            new()
            {
                Slug = "coffee-machines",
                Translations = new List<CategoryTranslation>
                {
                    new() { LanguageCode = "en", Name = "Coffee Machines" },
                    new() { LanguageCode = "ar", Name = "آلات القهوة" }
                }
            },
            new()
            {
                Slug = "cups-and-mugs",
                Translations = new List<CategoryTranslation>
                {
                    new() { LanguageCode = "en", Name = "Cups & Mugs" },
                    new() { LanguageCode = "ar", Name = "أكواب وأباريق" }
                }
            },
            new()
            {
                Slug = "coffee-accessories",
                Translations = new List<CategoryTranslation>
                {
                    new() { LanguageCode = "en", Name = "Coffee Accessories" },
                    new() { LanguageCode = "ar", Name = "ملحقات القهوة" }
                }
            },
            new()
            {
                Slug = "coffee-filters",
                Translations = new List<CategoryTranslation>
                {
                    new() { LanguageCode = "en", Name = "Coffee Filters" },
                    new() { LanguageCode = "ar", Name = "مرشحات القهوة" }
                }
            }
        };

        await context.Categories.AddRangeAsync(categories);
        await context.SaveChangesAsync();
        logger.LogInformation("Categories with translations seeded successfully.");
    }

    private static async Task SeedProductsAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        if (await context.Products.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding Products with Variant Architecture...");

        // Ensure Prerequisite Data Exists
        var coffeeBeansCategory = await context.Categories.FirstOrDefaultAsync(c => c.Slug == "coffee-beans");
        if (coffeeBeansCategory == null) return;

        var brazilOrigin = await context.Origins.FirstOrDefaultAsync(o => o.Name == "Brazil");
        var ethiopiaOrigin = await context.Origins.FirstOrDefaultAsync(o => o.Name == "Ethiopia");
        var colombiaOrigin = await context.Origins.FirstOrDefaultAsync(o => o.Name == "Colombia");
        var malaysiaOrigin = await context.Origins.FirstOrDefaultAsync(o => o.Name == "Malaysia");

        var mediumRoast = await context.Set<RoastLevelEntity>().FirstOrDefaultAsync(r => r.Name == "Medium");
        if (mediumRoast == null) { mediumRoast = new RoastLevelEntity { Name = "Medium" }; context.Add(mediumRoast); }

        var lightRoast = await context.Set<RoastLevelEntity>().FirstOrDefaultAsync(r => r.Name == "Light");
        if (lightRoast == null) { lightRoast = new RoastLevelEntity { Name = "Light" }; context.Add(lightRoast); }

        var wholeBean = await context.Set<GrindType>().FirstOrDefaultAsync(g => g.Name == "Whole Bean");
        if (wholeBean == null) { wholeBean = new GrindType { Name = "Whole Bean" }; context.Add(wholeBean); }

        var weightAttribute = new ProductAttribute { Id = Guid.NewGuid(), Name = "Weight" };
        var weight250g = new ProductAttributeValue { Id = Guid.NewGuid(), ProductAttributeId = weightAttribute.Id, Value = "250g" };
        var weight1kg = new ProductAttributeValue { Id = Guid.NewGuid(), ProductAttributeId = weightAttribute.Id, Value = "1kg" };

        context.ProductAttributes.Add(weightAttribute);
        context.ProductAttributeValues.AddRange(weight250g, weight1kg);

        await context.SaveChangesAsync();

        var products = new List<Product>
        {
            // 1. Brazil Lençóis
            new Product
            {
                Id = Guid.Parse("b8e278d7-0f7a-442f-bb10-8dc8d91a175c"),
                CategoryId = coffeeBeansCategory.Id,
                Slug = "brazil-lenis",
                IsActive = true,
                CreatedAt = DateTime.Parse("2026-03-13T17:52:38.791374Z").ToUniversalTime(),
                Translations = new List<ProductTranslation>
                {
                    new ProductTranslation
                    {
                        LanguageCode = "en",
                        Name = "Brazil Lençóis",
                        Description = "Brazil Lençóis is a smooth and well-balanced coffee grown in the rich soils of Brazil. It offers a naturally sweet profile with notes of chocolate, roasted nuts, and a hint of caramel. The medium body and low acidity make it an easy and enjoyable cup, perfect for both espresso and filter brewing. This coffee delivers a warm, comforting flavor with a clean finish."
                    },
                    new ProductTranslation
                    {
                        LanguageCode = "ar",
                        Name = "البرازيل لينسويس",
                        Description = "قهوة البرازيل لينسويس هي قهوة ناعمة ومتوازنة تُزرع في تربة البرازيل الغنية. تتميز بطعم طبيعي حلو مع نكهات الشوكولاتة والمكسرات المحمصة ولمسة خفيفة من الكراميل. قوامها متوسط وحموضتها منخفضة، مما يجعلها كوبًا سهل الشرب ومناسبًا لتحضير الإسبريسو أو القهوة المفلترة. تقدم هذه القهوة مذاقًا دافئًا ومريحًا مع نهاية نظيفة وممتعة"
                    }
                },
                CoffeeProfile = new ProductCoffeeProfile
                {
                    OriginId = brazilOrigin?.Id,
                    RoastLevels = new List<RoastLevelEntity> { mediumRoast },
                    GrindTypes = new List<GrindType> { wholeBean },
                    FlavourNotes = new List<FlavourNote>
                    {
                        new FlavourNote
                        {
                            DisplayOrder = 1,
                            Name = "Chocolate",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Chocolate" } }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 2,
                            Name = "Walnut",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Walnut" } }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 3,
                            Name = "Cookies",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Cookies" } }
                        }
                    }
                },
                Variants = new List<ProductVariant>
                {
                    new ProductVariant { Sku = "brazil-lenis-250g", Price = 56.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 56.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 15.25m } }, StockQuantity = 100, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight250g.Id } } },
                    new ProductVariant { Sku = "brazil-lenis-1kg", Price = 188.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 188.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 51.18m } }, StockQuantity = 100, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight1kg.Id } } }
                },
                Images = new List<ProductImage>
                {
                    new ProductImage { ImageUrl = "https://storage.googleapis.com/duneflame-images/products/a3e09dc0-1ec5-4ad3-8cff-d38eed9676ae_Brazil Lençois.jpg.jpeg", IsMain = true }
                }
            },
            // 2. Ethiopia Guji Hambela
            new Product
            {
                Id = Guid.Parse("c3ce8096-22ca-4fc5-88c2-4dc4ecdda191"),
                CategoryId = coffeeBeansCategory.Id,
                Slug = "ethiopia-guji-hambela",
                IsActive = true,
                CreatedAt = DateTime.Parse("2026-03-13T17:59:43.207652Z").ToUniversalTime(),
                Translations = new List<ProductTranslation>
                {
                    new ProductTranslation
                    {
                        LanguageCode = "en",
                        Name = "Ethiopia Guji Hambela",
                        Description = "Ethiopia Guji Hamebla is a vibrant and aromatic specialty coffee grown in the highlands of the Guji region. This coffee is known for its elegant floral character and bright sweetness. It offers delicate notes of jasmine and bergamot, complemented by soft floral tones and a juicy hint of peach. The cup is clean, complex, and beautifully balanced with a silky body and a refreshing finish."
                    },
                    new ProductTranslation
                    {
                        LanguageCode = "ar",
                        Name = "إثيوبيا غوجي هامبيلا",
                        Description = "قهوة إثيوبيا غوجي هامبيلا هي قهوة مختصة مميزة تُزرع في المرتفعات العالية في منطقة غوجي في إثيوبيا. تتميز هذه القهوة بعطرها الزهري الأنيق وحلاوتها المشرقة. تقدم نكهات رقيقة من الياسمين والبرغموت مع لمسات زهرية لطيفة وإشارة فاكهية من الخوخ. الكوب متوازن ونظيف ومعقد، بقوام ناعم ونهاية منعشة وممتعة."
                    }
                },
                CoffeeProfile = new ProductCoffeeProfile
                {
                    OriginId = ethiopiaOrigin?.Id,
                    RoastLevels = new List<RoastLevelEntity> { mediumRoast },
                    GrindTypes = new List<GrindType> { wholeBean },
                    FlavourNotes = new List<FlavourNote>
                    {
                        new FlavourNote
                        {
                            DisplayOrder = 1,
                            Name = "Jasmine",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Jasmine" } }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 2,
                            Name = "Floral",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Floral" } }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 3,
                            Name = "Peach",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Peach" } }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 4,
                            Name = "Bergamot",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Bergamot" } }
                        }
                    }
                },
                Variants = new List<ProductVariant>
                {
                    new ProductVariant { Sku = "ethiopia-guji-hambela-250g", Price = 63.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 63.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 17.15m } }, StockQuantity = 98, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight250g.Id } } },
                    new ProductVariant { Sku = "ethiopia-guji-hambela-1kg", Price = 214.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 214.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 58.26m } }, StockQuantity = 98, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight1kg.Id } } }
                },
                Images = new List<ProductImage>
                {
                    new ProductImage { ImageUrl = "https://storage.googleapis.com/duneflame-images/products/14fd206a-effe-437e-a05a-39cb55cec021_Ethiopia Hambela.jpg.jpeg", IsMain = true }
                }
            },
            // 3. Tutti Frutti
            new Product
            {
                Id = Guid.Parse("5198a987-b969-4ac3-b7c8-c96c590420ad"),
                CategoryId = coffeeBeansCategory.Id,
                Slug = "tutti-frutti",
                IsActive = true,
                CreatedAt = DateTime.Parse("2026-03-13T18:06:54.074036Z").ToUniversalTime(),
                Translations = new List<ProductTranslation>
                {
                    new ProductTranslation
                    {
                        LanguageCode = "en",
                        Name = "Tutti Frutti",
                        Description = "Tutti Frutti is a bright and lively coffee with a vibrant fruity profile. It offers fragrant notes of orange blossom, juicy mandarin, and a mix of tropical fruits. The cup is sweet, refreshing, and aromatic, with a smooth body and a pleasant, fruity finish that makes every sip feel lively and enjoyable."
                    },
                    new ProductTranslation
                    {
                        LanguageCode = "ar",
                        Name = "توتي فروتي",
                        Description = "قهوة توتي فروتي هي قهوة مشرقة وحيوية تتميز بطابع فاكهي غني. تقدم نكهات عطرية من زهر البرتقال واليوسفي العصيري ومزيج من الفواكه الاستوائية. الكوب حلو ومنعش وعطري، بقوام ناعم ونهاية فاكهية لطيفة تجعل كل رشفة ممتعة ومليئة بالحيوية.\r\n\r\n"
                    }
                },
                CoffeeProfile = new ProductCoffeeProfile
                {
                    OriginId = colombiaOrigin?.Id,
                    RoastLevels = new List<RoastLevelEntity> { lightRoast },
                    GrindTypes = new List<GrindType> { wholeBean },
                    FlavourNotes = new List<FlavourNote>
                    {
                        new FlavourNote
                        {
                            DisplayOrder = 1,
                            Name = "Orange Blossom",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Orange Blossom" } }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 2,
                            Name = "Mandarin",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Mandarin" } }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 3,
                            Name = "Tropical Fruit",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Tropical Fruit" } }
                        }
                    }
                },
                Variants = new List<ProductVariant>
                {
                    new ProductVariant { Sku = "tutti-frutti-250g", Price = 169.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 169.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 46.01m } }, StockQuantity = 98, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight250g.Id } } }
                },
                Images = new List<ProductImage>
                {
                    new ProductImage { ImageUrl = "https://storage.googleapis.com/duneflame-images/products/e13113ed-f855-49af-af53-5ce55269c56b_Tutti Frutti.jpg.jpeg", IsMain = true }
                }
            },
            // 4. Dokha
            new Product
            {
                Id = Guid.Parse("56af9d84-d249-4de3-97f2-c0046e15ac44"),
                CategoryId = coffeeBeansCategory.Id,
                Slug = "dokha",
                IsActive = true,
                CreatedAt = DateTime.Parse("2026-03-13T18:03:42.765985Z").ToUniversalTime(),
                Translations = new List<ProductTranslation>
                {
                    new ProductTranslation
                    {
                        LanguageCode = "en",
                        Name = "Dokha",
                        Description = "Dokha is a bold and distinctive blend crafted for those who appreciate rich and intense flavors. With notes of sweet tobacco, dark caramel, and woody spice, it delivers a deep and memorable sensory experience. Made from Liberica beans and sourced from Malaysia, it offers a unique profile that stands apart from traditional varieties. Perfect for those seeking a strong and unconventional experience."
                    },
                    new ProductTranslation
                    {
                        LanguageCode = "ar",
                        Name = "الدوخة",
                        Description = "الدوخة هي مزيج جريء ومميز لعشاق النكهات القوية والغنية. تتميز بنفحات من التبغ الحلو، والكراميل الداكن، والتوابل الخشبية، لتمنح تجربة فريدة لا تُنسى. مصنوعة من حبوب ليبيريكا ومصدرها ماليزيا، وتقدم طعماً مختلفاً عن الأنواع التقليدية. مثالية لمن يبحث عن تجربة قوية وغير تقليدية."
                    }
                },
                CoffeeProfile = new ProductCoffeeProfile
                {
                    OriginId = malaysiaOrigin?.Id,
                    RoastLevels = new List<RoastLevelEntity> { lightRoast },
                    GrindTypes = new List<GrindType> { wholeBean },
                    FlavourNotes = new List<FlavourNote>
                    {
                        new FlavourNote
                        {
                            DisplayOrder = 1,
                            Name = "Sweet Tobacco",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Sweet Tobacco" } }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 2,
                            Name = "Dark Caramel",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Dark Caramel" } }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 3,
                            Name = "Woody Spice",
                            Translations = new List<FlavourNoteTranslation> { new FlavourNoteTranslation { LanguageCode = "en", Name = "Woody Spice" } }
                        }
                    }
                },
                Variants = new List<ProductVariant>
                {
                    new ProductVariant { Sku = "dokha-250g", Price = 162.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 162.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 44.10m } }, StockQuantity = 100, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight250g.Id } } }
                },
                Images = new List<ProductImage>
                {
                    new ProductImage { ImageUrl = "https://storage.googleapis.com/duneflame-images/products/eee4a01f-deb2-4490-a194-9f5ae7642ce3_Dokha.jpg.jpeg", IsMain = true }
                }
            }
        };

        await context.Products.AddRangeAsync(products);
        await context.SaveChangesAsync();

        logger.LogInformation("Products with Variant Architecture seeded successfully.");
    }




    private static async Task SeedCmsContentAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        if (await context.AboutSections.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding CMS Content...");

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
        // Removed order seeding logic to allow proper EF Core migration to the new Variant Architecture.
        logger.LogInformation("Legacy order auto-seeding removed for Variant Architecture compatibility.");
        await Task.CompletedTask;
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



            // United Arab Emirates Cities/Regions (Emirates)
            var aeCountry = countries.FirstOrDefault(c => c.Code == "AE");
            if (aeCountry != null)
            {
                cities.AddRange(new[]
                {
                            new City { Name = "Abu Dhabi", CountryId = aeCountry.Id },
                            new City { Name = "Dubai", CountryId = aeCountry.Id },
                            new City { Name = "Sharjah", CountryId = aeCountry.Id },
                            new City { Name = "Ajman", CountryId = aeCountry.Id },
                            new City { Name = "Umm Al Quwain", CountryId = aeCountry.Id },
                            new City { Name = "Ras Al Khaimah", CountryId = aeCountry.Id },
                            new City { Name = "Fujairah", CountryId = aeCountry.Id }
                        });
            }



            // Kuwait Cities/Regions (Governorates)
            var kwCountry = countries.FirstOrDefault(c => c.Code == "KW");
            if (kwCountry != null)
            {
                cities.AddRange(new[]
                {
                            new City { Name = "Al Asimah (Capital)", CountryId = kwCountry.Id },
                            new City { Name = "Hawalli", CountryId = kwCountry.Id },
                            new City { Name = "Farwaniya", CountryId = kwCountry.Id },
                            new City { Name = "Ahmadi", CountryId = kwCountry.Id },
                            new City { Name = "Jahra", CountryId = kwCountry.Id },
                            new City { Name = "Mubarak Al-Kabeer", CountryId = kwCountry.Id }
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
            "SA" => 34.00m,
            "AE" => 6.81m,
            "QA" => 34.00m,
            "KW" => 34.00m,
            "BH" => 34.00m,
            "OM" => 34.00m,
            _ => 0m
        };
    }

    private static decimal GetShippingCostAED(string countryCode)
    {
        return countryCode switch
        {
            "SA" => 125.00m,
            "AE" => 25.00m,
            "QA" => 125.00m,
            "KW" => 125.00m,
            "BH" => 125.00m,
            "OM" => 125.00m,
            _ => 0m
        };
    }
}
