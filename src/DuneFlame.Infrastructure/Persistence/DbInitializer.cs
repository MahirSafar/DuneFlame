using DuneFlame.Domain.Entities;
using DuneFlame.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DuneFlame.Infrastructure.Persistence;

public static class DbInitializer
{
    /// <summary>
    /// Wraps SaveChangesAsync in a try-catch for DbUpdateException to handle
    /// concurrent container startup race conditions (e.g., Cloud Run).
    /// If two instances pass the AnyAsync check simultaneously and both attempt
    /// to insert the same master data, the loser of the race will catch the
    /// duplicate-key violation, log a warning, and continue — because the data
    /// was already committed by the winning instance.
    /// </summary>
    private static async Task TrySaveAsync(AppDbContext context, ILogger logger, string entityLabel)
    {
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate key") == true
                                        || ex.InnerException?.Message.Contains("unique constraint") == true
                                        || ex.InnerException?.Message.Contains("23505") == true)
        {
            logger.LogWarning("Concurrent seed detected for {Entity} — duplicate key ignored. Data was already committed by another instance.", entityLabel);
            // Detach all tracked entries so the context is usable again
            context.ChangeTracker.Clear();
        }
    }

    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        try
        {
            // Apply migrations or ensure database is created.
            // IsRelational() covers all relational providers (Postgres, SQL Server, SQLite, etc.)
            // and correctly excludes InMemory, which does not support migrations.
            if (context.Database.IsRelational())
            {
                await context.Database.MigrateAsync();
            }
            else
            {
                await context.Database.EnsureCreatedAsync();
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

            // 5.5 Seed Brands
            await SeedBrandsAsync(context, logger);

            // 6. Seed Products
            await SeedProductsAsync(context, logger);
            await SeedFiorenzatoGrindersAsync(context, logger);
            await SeedAccessoriesAsync(context, logger);

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
        await TrySaveAsync(context, logger, "AppSettings");
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
        await TrySaveAsync(context, logger, "Origins");
    }

    private static async Task SeedCategoriesAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        if (await context.Categories.AnyAsync())
        {
            return;
        }

        logger.LogInformation("Seeding Category hierarchy (Master Root / BFS wave pattern)...");

        // WAVE 0: Master Root — ParentCategoryId = Guid.Empty (sentinel, no DB row)
        // Fixed deterministic Id so children can reference it immediately after SaveChanges.
        var rootId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var root = new Category
        {
            Id = rootId,
            Slug = "root",
            IsCoffeeCategory = false,
            ParentCategoryId = rootId,
            Translations =
            [
                new() { LanguageCode = "en", Name = "All Products" },
                new() { LanguageCode = "ar", Name = "جميع المنتجات" }
            ]
        };

        await context.Categories.AddAsync(root);
        await TrySaveAsync(context, logger, "Category:root"); // Commit root BEFORE any child references Guid.Empty FK

        // slug → Guid lookup — populated after each wave so children resolve parent Ids instantly
        var slugToId = new Dictionary<string, Guid> { ["root"] = rootId };

        // Flat tree definition: (Slug, NameEn, NameAr, IsCoffee, ParentSlug)
        // Ordered top-down — each entry's ParentSlug is always resolved before we reach it.
        var treeDefinition = new (string Slug, string NameEn, string NameAr, bool IsCoffee, string ParentSlug)[]
        {
            // WAVE 1 — L1 children of root
            ("coffee",              "Coffee",              "قهوة",              true,  "root"),
            ("equipment",           "Equipment",           "معدات",             false, "root"),

            // WAVE 2a — L2 children of coffee
            ("whole-beans",         "Whole Beans",         "حبوب كاملة",        true,  "coffee"),
            ("drip-bags",           "Drip Bags",           "أكياس تقطير",       true,  "coffee"),
            ("capsules",            "Capsules",            "كبسولات",           true,  "coffee"),

            // WAVE 2b — L2 children of equipment
            ("coffee-machines",     "Coffee Machines",     "آلات القهوة",       false, "equipment"),
            ("grinders",            "Grinders",            "مطاحن",             false, "equipment"),
            ("brewing-accessories", "Brewing Accessories", "ملحقات التحضير",    false, "equipment"),

            // WAVE 3a — L3 children of coffee-machines
            ("professional-coffee-machines", "Professional Coffee Machines", "آلات قهوة احترافية", false, "coffee-machines"),
            ("home-coffee-machines",         "Home Coffee Machines",         "آلات قهوة منزلية",  false, "coffee-machines"),

            // WAVE 3b — L3 children of grinders
            ("professional-coffee-grinders", "Professional Coffee Grinders", "مطاحن احترافية", false, "grinders"),
            ("home-coffee-grinders",         "Home Coffee Grinders",         "مطاحن منزلية",   false, "grinders"),
            ("manual-coffee-grinders",       "Manual Coffee Grinders",       "مطاحن يدوية",    false, "grinders"),

            // WAVE 3c — L3 children of brewing-accessories
            ("brewing-equipment",    "Brewing Equipment",    "معدات التحضير",         false, "brewing-accessories"),
            ("drinkware-storage",    "Drinkware & Storage",  "أدوات الشرب والتخزين",  false, "brewing-accessories"),
            ("cleaning-maintenance", "Cleaning Maintenance", "التنظيف والصيانة",      false, "brewing-accessories"),
        };

        // BFS wave order — each entry is a parent slug whose children form one committed batch.
        // Parents must be committed before children reference them as FKs.
        var waveOrder = new[] { "root", "coffee", "equipment", "coffee-machines", "grinders", "brewing-accessories" };

        var byParent = treeDefinition.GroupBy(n => n.ParentSlug)
                                     .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var parentSlug in waveOrder)
        {
            if (!byParent.TryGetValue(parentSlug, out var wave)) continue;

            var batch = new List<Category>();
            foreach (var node in wave)
            {
                var category = new Category
                {
                    Id = Guid.NewGuid(),
                    Slug = node.Slug,
                    IsCoffeeCategory = node.IsCoffee,
                    ParentCategoryId = slugToId[node.ParentSlug],
                    Translations =
                    [
                        new() { LanguageCode = "en", Name = node.NameEn },
                        new() { LanguageCode = "ar", Name = node.NameAr }
                    ]
                };
                batch.Add(category);
                slugToId[node.Slug] = category.Id; // register for next wave
            }

            await context.Categories.AddRangeAsync(batch);
            await TrySaveAsync(context, logger, $"Categories:wave({parentSlug})"); // Commit each wave before its children are processed
        }

        logger.LogInformation("Category hierarchy seeded: Root → L1 (2) → L2 (6) → L3 (8). Total: 17 categories.");
    }

    private static async Task SeedBrandsAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        if (await context.Brands.AnyAsync()) return;

        logger.LogInformation("Seeding Brands...");
        var brands = new List<Brand>
        {
            new Brand { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "DuneFlame", Description = "Premium selection of freshly roasted coffees", IsActive = true },
            new Brand { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = "Fiorenzato", Description = "Professional coffee grinders", IsActive = true },
            new Brand { Id = Guid.Parse("55555555-5555-5555-5555-555555555555"), Name = "MHW-3BOMBER", Description = "Professional coffee accessories and equipment", IsActive = true }
        };

        await context.Brands.AddRangeAsync(brands);
        await TrySaveAsync(context, logger, "Brands");
        logger.LogInformation("Brands seeded successfully.");
    }

    private static async Task SeedProductsAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        // Granular idempotency: check FlavourNotes independently of Products so that
        // stale DB state (FlavourNotes committed but Products not, or vice-versa) is handled.
        if (await context.Products.AnyAsync(p => p.BrandId != Guid.Parse("44444444-4444-4444-4444-444444444444") && p.BrandId != Guid.Parse("55555555-5555-5555-5555-555555555555")))
        {
            logger.LogInformation("Coffee products already seeded. Skipping SeedProductsAsync.");
            return;
        }

        // Secondary guard: if FlavourNotes already exist, a previous instance seeded the
        // products graph and then crashed before the Products check could pass. Skip to avoid
        // duplicate FlavourNoteTranslation violations.
        if (await context.Set<FlavourNote>().AnyAsync())
        {
            logger.LogInformation("FlavourNotes already exist (partial prior seed). Skipping SeedProductsAsync.");
            return;
        }

        logger.LogInformation("Seeding Products with Variant Architecture...");

        // Ensure Prerequisite Data Exists
        var coffeeBeansCategory = await context.Categories.FirstOrDefaultAsync(c => c.Slug == "whole-beans")
            ?? throw new InvalidOperationException("Seeding prerequisite missing: Category with slug 'whole-beans' was not found. Ensure SeedCategoriesAsync completed successfully before SeedProductsAsync.");

        var brazilOrigin = await context.Origins.FirstOrDefaultAsync(o => o.Name == "Brazil");
        var ethiopiaOrigin = await context.Origins.FirstOrDefaultAsync(o => o.Name == "Ethiopia");
        var colombiaOrigin = await context.Origins.FirstOrDefaultAsync(o => o.Name == "Colombia");
        var malaysiaOrigin = await context.Origins.FirstOrDefaultAsync(o => o.Name == "Malaysia");

        // --- Granular get-or-create guards for all master data ---

        var mediumRoast = await context.Set<RoastLevelEntity>().FirstOrDefaultAsync(r => r.Name == "Medium");
        if (mediumRoast == null) { mediumRoast = new RoastLevelEntity { Name = "Medium" }; context.Add(mediumRoast); }

        var lightRoast = await context.Set<RoastLevelEntity>().FirstOrDefaultAsync(r => r.Name == "Light");
        if (lightRoast == null) { lightRoast = new RoastLevelEntity { Name = "Light" }; context.Add(lightRoast); }

        var wholeBean = await context.Set<GrindType>().FirstOrDefaultAsync(g => g.Name == "Whole Bean");
        if (wholeBean == null) { wholeBean = new GrindType { Name = "Whole Bean" }; context.Add(wholeBean); }

        // Flush RoastLevels and GrindTypes before building the product graph so they
        // have stable DB-assigned IDs that EF Core can use for the join tables.
        await TrySaveAsync(context, logger, "RoastLevels/GrindTypes");

        // Re-query after potential concurrent save so we hold the committed rows.
        mediumRoast = await context.Set<RoastLevelEntity>().FirstOrDefaultAsync(r => r.Name == "Medium") ?? mediumRoast;
        lightRoast  = await context.Set<RoastLevelEntity>().FirstOrDefaultAsync(r => r.Name == "Light")  ?? lightRoast;
        wholeBean   = await context.Set<GrindType>().FirstOrDefaultAsync(g => g.Name == "Whole Bean")    ?? wholeBean;

        // Get-or-create Weight attribute and its values to prevent duplicate inserts on retry
        var weightAttribute = await context.ProductAttributes.FirstOrDefaultAsync(a => a.Name == "Weight");
        if (weightAttribute == null)
        {
            weightAttribute = new ProductAttribute { Id = Guid.NewGuid(), Name = "Weight" };
            context.ProductAttributes.Add(weightAttribute);
            await TrySaveAsync(context, logger, "ProductAttribute:Weight");
            // Re-query in case the concurrent winner committed the row
            weightAttribute = await context.ProductAttributes.FirstOrDefaultAsync(a => a.Name == "Weight") ?? weightAttribute;
        }

        var weight250g = await context.ProductAttributeValues
            .FirstOrDefaultAsync(v => v.ProductAttributeId == weightAttribute.Id && v.Value == "250g");
        if (weight250g == null)
        {
            weight250g = new ProductAttributeValue { Id = Guid.NewGuid(), ProductAttributeId = weightAttribute.Id, Value = "250g" };
            context.ProductAttributeValues.Add(weight250g);
        }

        var weight1kg = await context.ProductAttributeValues
            .FirstOrDefaultAsync(v => v.ProductAttributeId == weightAttribute.Id && v.Value == "1kg");
        if (weight1kg == null)
        {
            weight1kg = new ProductAttributeValue { Id = Guid.NewGuid(), ProductAttributeId = weightAttribute.Id, Value = "1kg" };
            context.ProductAttributeValues.Add(weight1kg);
        }

        await TrySaveAsync(context, logger, "ProductAttributeValues:Weight");

        // Re-query attribute values after potential concurrent save
        weight250g = await context.ProductAttributeValues
            .FirstOrDefaultAsync(v => v.ProductAttributeId == weightAttribute.Id && v.Value == "250g") ?? weight250g;
        weight1kg = await context.ProductAttributeValues
            .FirstOrDefaultAsync(v => v.ProductAttributeId == weightAttribute.Id && v.Value == "1kg") ?? weight1kg;

        var products = new List<Product>
        {
            // 1. Brazil Lençóis
            new Product
            {
                Id = Guid.Parse("b8e278d7-0f7a-442f-bb10-8dc8d91a175c"),
                CategoryId = coffeeBeansCategory.Id, BrandId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
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
                }
            },
            // 2. Ethiopia Guji Hambela
            new Product
            {
                Id = Guid.Parse("c3ce8096-22ca-4fc5-88c2-4dc4ecdda191"),
                CategoryId = coffeeBeansCategory.Id, BrandId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
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
                }
            },
            // 3. Tutti Frutti
            new Product
            {
                Id = Guid.Parse("5198a987-b969-4ac3-b7c8-c96c590420ad"),
                CategoryId = coffeeBeansCategory.Id, BrandId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
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
                }
            },
            // 4. Dokha
            new Product
            {
                Id = Guid.Parse("56af9d84-d249-4de3-97f2-c0046e15ac44"),
                CategoryId = coffeeBeansCategory.Id, BrandId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
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
                }
            }
        };

        await context.Products.AddRangeAsync(products);
        await TrySaveAsync(context, logger, "Products+FlavourNotes");

        logger.LogInformation("Products with Variant Architecture seeded successfully.");
    }




    private static async Task SeedAccessoriesAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        var mhwBrand = await context.Brands.FirstOrDefaultAsync(b => b.Name.Contains("MHW"));
        if (mhwBrand == null)
        {
            mhwBrand = new Brand { Id = Guid.NewGuid(), Name = "MHW-3BOMBER", Description = "Professional coffee accessories and equipment", IsActive = true };
            context.Brands.Add(mhwBrand);
            await TrySaveAsync(context, logger, "MHW Brand");
            mhwBrand = await context.Brands.FirstOrDefaultAsync(b => b.Name.Contains("MHW")) ?? mhwBrand;
        }

        if (await context.Products.AnyAsync(p => p.BrandId == mhwBrand.Id))
        {
            logger.LogInformation("MHW accessory products already seeded. Skipping SeedAccessoriesAsync.");
            return;
        }

        logger.LogInformation("Seeding MHW Accessories...");

        var brewingEquipmentCat = await context.Categories
            .FirstOrDefaultAsync(c => c.Translations.Any(t => t.LanguageCode == "en" && t.Name == "Brewing Equipment"))
            ?? throw new InvalidOperationException("Seeding prerequisite missing: Category 'Brewing Equipment' not found.");

        var drinkwareStorageCat = await context.Categories
            .FirstOrDefaultAsync(c => c.Translations.Any(t => t.LanguageCode == "en" && t.Name == "Drinkware & Storage"))
            ?? throw new InvalidOperationException("Seeding prerequisite missing: Category 'Drinkware & Storage' not found.");

        var cleaningMaintenanceCat = await context.Categories
            .FirstOrDefaultAsync(c => c.Translations.Any(t => t.LanguageCode == "en" && t.Name == "Cleaning Maintenance"))
            ?? throw new InvalidOperationException("Seeding prerequisite missing: Category 'Cleaning Maintenance' not found.");

        Category GetCategory(string name) => name switch
        {
            "Brewing Equipment"    => brewingEquipmentCat,
            "Drinkware & Storage" => drinkwareStorageCat,
            "Cleaning Maintenance" => cleaningMaintenanceCat,
            _ => throw new InvalidOperationException($"Unknown accessory category: {name}")
        };

        ProductAttribute? colorAttribute = await context.ProductAttributes.FirstOrDefaultAsync(a => a.Name == "Color");

        async Task<ProductAttributeValue> GetOrCreateColorValue(string colorName)
        {
            if (colorAttribute == null)
            {
                colorAttribute = new ProductAttribute { Id = Guid.NewGuid(), Name = "Color" };
                context.ProductAttributes.Add(colorAttribute);
                await TrySaveAsync(context, logger, "ProductAttribute:Color");
                colorAttribute = await context.ProductAttributes.FirstOrDefaultAsync(a => a.Name == "Color") ?? colorAttribute;
            }

            var val = await context.ProductAttributeValues
                .FirstOrDefaultAsync(v => v.ProductAttributeId == colorAttribute.Id && v.Value == colorName);
            if (val != null) return val;

            val = new ProductAttributeValue { Id = Guid.NewGuid(), ProductAttributeId = colorAttribute.Id, Value = colorName };
            context.ProductAttributeValues.Add(val);
            await TrySaveAsync(context, logger, $"ProductAttributeValue:Color:{colorName}");
            return await context.ProductAttributeValues
                .FirstOrDefaultAsync(v => v.ProductAttributeId == colorAttribute.Id && v.Value == colorName) ?? val;
        }

        // (Name, CategoryName, Variants: (Color, AED, USD)[])
        // Color = "N/A" means no Color attribute — one default variant.
        var accessories = new (string Name, string CategoryName, (string Color, decimal AED, decimal USD)[] Variants)[]
        {
            ("MHW GT Milk Pitcher-400ml-U-shape spout",                  "Brewing Equipment",    new[] { ("Glossy",        133m,  35.91m) }),
            ("MHW Milk pitcher 5.0-500ml",                               "Brewing Equipment",    new[] { ("Multicolor",    133m,  35.91m), ("Glossy",        109m,  29.43m), ("Silver Spot",   133m,  35.91m) }),
            ("MHW Turbo milk pitcher-450ml",                             "Brewing Equipment",    new[] { ("Glossy",        109m,  29.43m), ("Silver Spot",   133m,  35.91m) }),
            ("MHW OZ Cup-50ml",                                          "Drinkware & Storage",  new[] { ("N/A",            31m,   8.37m) }),
            ("MHW Stainless Steel Measuring Cup-100ml",                  "Brewing Equipment",    new[] { ("matte black",    47m,  12.69m) }),
            ("MHW Flash Tamper 2.0-58mm-Flat 2.0",                      "Brewing Equipment",    new[] { ("N/A",           191m,  51.57m) }),
            ("MHW Vase Tamper-58mm universal-Flat",                     "Brewing Equipment",    new[] { ("walnut",         72m,  19.44m) }),
            ("MHW Cyclone Gravity Coffee Distributor-58mm universal",   "Brewing Equipment",    new[] { ("N/A",           269m,  72.63m) }),
            ("MHW Astra Collection-Portafilter Holder-51-58mm",         "Brewing Equipment",    new[] { ("Black",         226m,  61.02m) }),
            ("MHW Cube Coffee Scale 2.0 Mini",                          "Brewing Equipment",    new[] { ("Black",         140m,  37.80m) }),
            ("MHW Cube Coffee Scale 3.0 Pro Max",                       "Brewing Equipment",    new[] { ("Black",         215m,  58.05m) }),
            ("MHW Formula Smart Coffee Scale",                          "Brewing Equipment",    new[] { ("Black",         195m,  52.65m), ("White",         195m,  52.65m) }),
            ("MHW Coffee Knock Box-6.8L",                               "Cleaning Maintenance", new[] { ("black",         503m, 135.81m) }),
            ("MHW Coffee Knock Box Garbage Bag-30pcs",                  "Cleaning Maintenance", new[] { ("N/A",            74m,  19.98m) }),
            ("MHW Magnetic Dosing Ring-58mm universal",                 "Brewing Equipment",    new[] { ("N/A",            59m,  15.93m) }),
            ("MHW Assassin electric pour over kettle",                  "Brewing Equipment",    new[] { ("White",         429m, 115.83m), ("Black",         429m, 115.83m) }),
            ("MHW Smooth Espresso Chilling Ball Stand-1pcs",            "Brewing Equipment",    new[] { ("Black",          70m,  18.90m), ("Silver",         70m,  18.90m) }),
            ("MHW Elf Coffee Server-500ml",                             "Drinkware & Storage",  new[] { ("Transparent",    59m,  15.93m) }),
            ("MHW Snail Filter Paper Holder",                           "Drinkware & Storage",  new[] { ("black",          70m,  18.90m) }),
            ("MHW Paper Filter-100pcs/box-V02",                         "Brewing Equipment",    new[] { ("N/A",            51m,  13.77m) }),
            ("MHW Paper Filter-100pcs/box-V01",                         "Brewing Equipment",    new[] { ("N/A",            39m,  10.53m) }),
            ("MHW Silicone Dish Drying Pad-400300mm",                   "Cleaning Maintenance", new[] { ("black",         125m,  33.75m) }),
            ("MHW YU Series-Tamping Base",                              "Brewing Equipment",    new[] { ("Black",         109m,  29.43m) }),
            ("MHW Filter Basket-58mm universal-18g",                    "Brewing Equipment",    new[] { ("N/A",            47m,  12.69m) }),
            ("MHW BEP Butterfly Basket 2.0-58mm universal-20g 2.0",    "Brewing Equipment",    new[] { ("N/A",            62m,  16.74m) }),
            ("MHW BEP Butterfly Basket 2.0-51mm universal-12g 2.0",    "Brewing Equipment",    new[] { ("N/A",            62m,  16.74m) }),
            ("MHW Elbow Brush-220mm-PA",                                "Cleaning Maintenance", new[] { ("Black",          31m,   8.37m) }),
            ("MHW Blind Bowl-58mm universal",                           "Cleaning Maintenance", new[] { ("silver spot",    31m,   8.37m) }),
            ("MHW Waffle towels-3030cm",                                "Cleaning Maintenance", new[] { ("dark grey",      27m,   7.29m) }),
            ("MHW Hanging Ring Towel-3050cm",                           "Cleaning Maintenance", new[] { ("black",          31m,   8.37m) }),
            ("MHW Cup Rinser-32.517.5cm-(S)",                           "Cleaning Maintenance", new[] { ("silver",        293m,  79.11m) }),
            ("MHW Embedded Stainless Steel Cup Rinser-19.5x59.5cm",    "Cleaning Maintenance", new[] { ("silver",        858m, 231.66m) }),
            ("MHW Milk pitcher 3.0-600ml-sharp spout",                  "Brewing Equipment",    new[] { ("Matte Black",   144m,  38.88m) }),
            ("MHW Wright Cup-90ml",                                     "Drinkware & Storage",  new[] { ("transparent",    55m,  14.85m) }),
            ("MHW Cream Double-layer Cup-300ML",                        "Drinkware & Storage",  new[] { ("N/A",           109m,  29.43m) }),
            ("MHW CERA Series Ceramic Cup-240ml",                       "Drinkware & Storage",  new[] { ("Black",         140m,  37.80m) }),
            ("MHW DW Glass-A-Shaped-130ml",                             "Drinkware & Storage",  new[] { ("Transparent",    78m,  21.06m), ("Black",          94m,  25.38m) }),
            ("MHW DW Glass-H-Shaped-160ml",                             "Drinkware & Storage",  new[] { ("Transparent",    78m,  21.06m), ("Black",          94m,  25.38m) }),
            ("MHW Yu Series-Unibody Bottomless Portafilter-Flat-58mm", "Brewing Equipment",    new[] { ("N/A",           772m, 208.44m) }),
            ("MHW SE Pro Needle Distributor-58mm",                      "Brewing Equipment",    new[] { ("Space Silver",  566m, 152.82m), ("Obsidian Black", 566m, 152.82m) }),
            ("MHW Coffee scale stand",                                  "Brewing Equipment",    new[] { ("Black",         195m,  52.65m) }),
            ("MHW RDT Spray Bottle",                                    "Brewing Equipment",    new[] { ("Black",          23m,   6.21m) }),
            ("MHW Coffee Bean Dosing Cup",                              "Brewing Equipment",    new[] { ("Black",          47m,  12.69m) }),
            ("MHW Coffee Dosing Ring-58mm universal",                   "Brewing Equipment",    new[] { ("silver",         70m,  18.90m) }),
            ("MHW B-3 Tactics Set-12 pcs in one",                      "Brewing Equipment",    new[] { ("Black",        1736m, 468.72m) }),
            ("MHW Smooth Espresso Chilling Ball Stand-2pcs",            "Brewing Equipment",    new[] { ("Black",         125m,  33.75m), ("Silver",        125m,  33.75m) }),
            ("MHW MET Outdoor Cassette Stove",                          "Brewing Equipment",    new[] { ("Metal Color",   351m,  94.77m) }),
            ("MHW Gimme French Press-500ml",                            "Brewing Equipment",    new[] { ("black",          62m,  16.74m), ("white",          62m,  16.74m) }),
            ("MHW Leather sheath sealed canister-600ml",                "Drinkware & Storage",  new[] { ("N/A",            62m,  16.74m) }),
            ("MHW Energy Coffee Beans Storage Tubes Set-(20-26g)x8",   "Drinkware & Storage",  new[] { ("N/A",           293m,  79.11m) }),
            ("MHW Air Blower",                                          "Cleaning Maintenance", new[] { ("silicone",       47m,  12.69m) }),
            ("MHW Cofffee Bar Brush",                                   "Cleaning Maintenance", new[] { ("Black",          78m,  21.06m) }),
            ("MHW Long Measuring Spoon-stainless steel-8g",             "Brewing Equipment",    new[] { ("silver spot",    47m,  12.69m) }),
            ("MHW Coffee Spoon-stainless steel",                        "Brewing Equipment",    new[] { ("silver spot",    23m,   6.21m) }),
            ("MHW Coffee Extraction Observation Mirror",                "Brewing Equipment",    new[] { ("Black",          94m,  25.38m) }),
        };

        var products = new List<Product>();
        foreach (var item in accessories)
        {
            var slug = item.Name.ToLower()
                .Replace(" ", "-").Replace("(", "").Replace(")", "")
                .Replace(",", "").Replace("/", "-").Replace("【", "")
                .Replace("】", "").Replace("*", "").Replace(".", "")
                .Replace("®", "").Replace(":", "");

            var variants = new List<ProductVariant>();
            foreach (var (color, aed, usd) in item.Variants)
            {
                var isColorVariant = !string.IsNullOrEmpty(color) && color != "N/A";
                var skuSuffix = isColorVariant ? color.Replace(" ", "") : "default";
                var variant = new ProductVariant
                {
                    Id = Guid.NewGuid(),
                    Sku = $"{slug}-{skuSuffix}".ToUpper(),
                    Price = aed,
                    StockQuantity = 10,
                    Prices = new List<ProductVariantPrice>
                    {
                        new() { Currency = Currency.AED, Price = aed },
                        new() { Currency = Currency.USD, Price = usd }
                    },
                    Options = new List<ProductVariantOption>()
                };

                if (isColorVariant)
                {
                    var colorVal = await GetOrCreateColorValue(color);
                    variant.Options.Add(new ProductVariantOption { ProductAttributeValueId = colorVal.Id });
                }

                variants.Add(variant);
            }

            products.Add(new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = GetCategory(item.CategoryName).Id,
                BrandId = mhwBrand.Id,
                Slug = slug,
                IsActive = true,
                Translations = new List<ProductTranslation>
                {
                    new() { LanguageCode = "en", Name = item.Name }
                },
                Variants = variants
            });
        }

        await context.Products.AddRangeAsync(products);
        await TrySaveAsync(context, logger, "Accessories");
        logger.LogInformation("MHW Accessories seeded successfully: {Count} products.", products.Count);
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
        await TrySaveAsync(context, logger, "CmsContent:AboutSections");
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

        await TrySaveAsync(context, logger, "MarketingData");
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
            await TrySaveAsync(context, logger, "Countries");
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
            await TrySaveAsync(context, logger, "Cities");
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
            await TrySaveAsync(context, logger, "ShippingRates");
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

    private static async Task SeedFiorenzatoGrindersAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        var category = await context.Categories.FirstOrDefaultAsync(c => c.Slug == "professional-coffee-grinders");
        var brand = await context.Brands.FirstOrDefaultAsync(b => b.Name == "Fiorenzato");

        if (category == null)
            throw new InvalidOperationException("Seeding prerequisite missing: Category with slug 'professional-coffee-grinders' was not found. Ensure SeedCategoriesAsync completed successfully before SeedFiorenzatoGrindersAsync.");

        if (brand == null)
            throw new InvalidOperationException("Seeding prerequisite missing: Brand 'Fiorenzato' was not found. Ensure SeedBrandsAsync completed successfully before SeedFiorenzatoGrindersAsync.");

        if (await context.Products.AnyAsync(p => p.BrandId == brand.Id))
        {
            logger.LogInformation("Fiorenzato grinder products already seeded. Skipping SeedFiorenzatoGrindersAsync.");
            return;
        }

        logger.LogInformation("Seeding Fiorenzato Grinders...");

        var colorAttribute = await context.ProductAttributes.FirstOrDefaultAsync(a => a.Name == "Color");
        if (colorAttribute == null)
        {
            colorAttribute = new ProductAttribute { Name = "Color" };
            context.ProductAttributes.Add(colorAttribute);
            await TrySaveAsync(context, logger, "ProductAttribute:Color");
            colorAttribute = await context.ProductAttributes.FirstOrDefaultAsync(a => a.Name == "Color") ?? colorAttribute;
        }

        async Task<ProductAttributeValue> GetOrAddColorFallback(string colorName)
        {
            var val = await context.ProductAttributeValues.FirstOrDefaultAsync(v => v.ProductAttributeId == colorAttribute.Id && v.Value == colorName);
            if (val == null)
            {
                val = new ProductAttributeValue { ProductAttributeId = colorAttribute.Id, Value = colorName };
                context.ProductAttributeValues.Add(val);
                await TrySaveAsync(context, logger, $"ProductAttributeValue:Color:{colorName}");
                val = await context.ProductAttributeValues.FirstOrDefaultAsync(v => v.ProductAttributeId == colorAttribute.Id && v.Value == colorName) ?? val;
            }
            return val;
        }

        var grinders = new List<(string Name, string NameAr, string Description, string DescriptionAr, decimal PriceAED, decimal PriceUSD, string[] Colors, Dictionary<string, string> Specs)>
        {
            ("F83 E XGI PRO", "F83 E XGI برو", "The F83 E XGi PRO is the most advanced and powerful grinder in the Fiorenzato catalog. It features the XGi weighing technology and the PRO maintenance system, but utilizes larger 83mm flat burrs for maximum speed and superior thermal stability. It is the ultimate tool for elite cafes, offering the highest level of dosing precision (by weight) and the fastest workflow, all while being incredibly easy to clean and service.", "تُعد F83 E XGi PRO المطحنة الأكثر تقدماً وقوة في كتالوج فيورينزاتو. تتميز بتقنية الوزن XGi ونظام الصيانة PRO، ولكنها تستخدم شفرات مسطحة أكبر مقاس 83 ملم لتحقيق أقصى سرعة واستقرار حراري فائق. إنها الأداة المثالية للمقاهي النخبوية، حيث توفر أعلى مستوى من دقة الجرعات (بالوزن) وأسرع تدفق عمل، مع كونها سهلة التنظيف والصيانة بشكل لا يصدق.", 
            11200.00m, 3049.69m, new[] { "Glossy Black" }, new() {
                {"Burrs", "Ø 83 mm flat coated burrs (titanium-aluminum-carbonnitrogen)"},
                {"Grind-by-Weight (GbW)", "XGi load-cell technology delivers precise dosing in grams via a responsive touchscreen (4.3″ IPS display)"},
                {"Motor Power & Speed", "650 W motor spinning at 1,350 RPM (50 Hz) and 1,550 RPM (60 Hz)"},
                {"Hopper Capacity", "Holds ~1.5 kg (3.3 lbs) of beans"},
                {"Weight & Dimensions", "Approx. 20 kg (44 lbs); footprint ~255 × 310 × 700 mm (10 × 12.2 × 27.5 in)"},
                {"Maintenance", "PRO quick-release grinding chamber—clean or remove burrs without resetting grind settings"},
                {"Touchscreen", "allows programmable single/double/triple/manual dosing"},
                {"Throughput", "up to ~300 drinks/day"}
            }),
            ("F64 E XGI PRO", "F64 E XGI برو", "The F64 E XGi PRO is the pinnacle of professional grinding technology, combining the patented XGi Grind-by-Weight system with the PRO detachable chamber. The XGi system uses advanced algorithms to calculate the exact weight of coffee ground in grams, while the PRO feature allows the entire grinding head to be removed for cleaning without affecting the grind size. This model provides unparalleled consistency and ease of maintenance for specialty coffee shops.", "تمثل F64 E XGi PRO قمة تكنولوجيا الطحن الاحترافي، حيث تجمع بين نظام XGi (الطحن بالوزن) الحاصل على براءة اختراع و حجرة الطحن القابلة للفصل (PRO). يستخدم نظام XGi خوارزميات متطورة لحساب الوزن الدقيق للقهوة المطحونة بالجرامات، بينما تتيح ميزة PRO إزالة رأس الطحن بالكامل للتنظيف دون التأثير على درجة الطحن. يوفر هذا الموديل تناسقاً لا مثيل له وسهولة في الصيانة لمقاهي القهوة المختصة.", 
            9400.00m, 2559.56m, new[] { "Glossy Black" }, new() {
                {"Burrs", "64 mm flat coated steel burrs (titanium-aluminum-carbonnitrogen blend)"},
                {"Grinding System", "Grind-by-Weight with XGi tech – dose by grams, highly precise via load cell"},
                {"Motor", "600 W, running at 1,350 RPM (50 Hz) or 1,550 RPM (60 Hz)"},
                {"Cooling", "EVO automatic fan cooling to maintain optimal bean temperature"},
                {"Dosing & Display", "Smart touchscreen (approx. 4.3″ IPS) with programmable single/double/triple doses, dose stats, burr wear indicator"},
                {"Maintenance", "PRO quick-release grinding chamber—clean or remove burrs without resetting grind settings"},
                {"Capacity & Output", "1.5 kg hopper (~3.3 lbs); suitable for ~3 kg/day throughput"},
                {"Dimensions", "~255 × 635 × 320 mm (10″ × 25″ × 12.6″)"},
                {"Weight", "~15–18 kg (33–40 lbs)"}
            }),
            ("F 83 E SENSE", "F 83 E سينس", "The F83 E Sense combines the massive output of 83mm burrs with the precision of integrated weighing technology. This model is designed for high-traffic environments where speed and accuracy are non-negotiable. The built-in scale allows for real-time dose monitoring, ensuring that every double shot is identical to the last. With its large touchscreen interface and high-speed motor, it represents the next generation of high-volume, precision espresso grinding.", "تجمع F83 E Sense بين الإنتاجية الضخمة لشفرات الـ 83 ملم ودقة تقنية الوزن المدمجة. صُمم هذا الموديل للبيئات المزدحمة حيث لا مجال للمساومة على السرعة والدقة. يسمح الميزان المدمج بمراقبة الجرعة في الوقت الفعلي، مما يضمن تطابق كل جرعة مع التي سبقتها تماماً. بفضل واجهة شاشة اللمس الكبيرة والمحرك عالي السرعة، تمثل هذه المطحنة الجيل القادم من مطاحن الإسبريسو عالية الدقة والإنتاجية.", 
            
            10500.00m, 2859.09m, new[] { "Matt Black" }, new() {
                {"Burrs", "Flat 83 mm M340 hardened steel burrs"},
                {"Motor Power", "650 W – 1,350 RPM (50 Hz) / 1,550 RPM (60 Hz)"},
                {"Integrated Scale", "Precise Grind-by-Weight (GbW) system accurate to 0.1 g, allowing dosing by weight or time"},
                {"Hopper Capacity", "~1.5 kg (3.3 lbs)"},
                {"Grinding Output", "Approx. 3.1 g/sec (suitable for high-volume cafes)"},
                {"Touch screen", "3.5″ color Cap Sense display for programming single, double, or triple doses, and viewing stats"},
                {"Burr Life", "Up to 4,500 kg of coffee before replacement"},
                {"Dimensions & Weight", "23 cm (W) x 68 cm (H) x 27 cm (D); approx. 15 kg (33 lbs)"}
            }),
            ("F64 EVO Sense", "F64 EVO سينس", "English Description: The F64 EVO Sense introduces high-precision Grind-by-Weight technology to the industry-standard F64 platform. By integrating a sophisticated scale into the portafilter holder, the Sense technology ensures that the grinder delivers the exact dose in grams every time, automatically compensating for changes in bean density or environment. It retains the signature 64mm burrs and cooling fan of the EVO series, making it the perfect choice for baristas who demand absolute precision without using an external scale.", "تقدم F64 EVO Sense تقنية الطحن بالوزن عالية الدقة إلى فئة F64 القياسية. من خلال دمج ميزان متطور في حامل البورتافلتر، تضمن تقنية Sense تقديم الجرعة الدقيقة بالجرامات في كل مرة، حيث تقوم تلقائياً بالتعويض عن التغيرات في كثافة البن أو العوامل البيئية. تحتفظ المطحنة بشفرات 64 ملم ومروحة التبريد الخاصة بسلسلة EVO، مما يجعلها الخيار الأمثل للباريستا الذين يبحثون عن الدقة المطلقة دون الحاجة لميزان خارجي.", 
            6780.00m, 1846.15m, new[] { "Glossy Black", "White" }, new() {
                {"Burrs", "64 mm flat"},
                {"Motor & Speed", "450 W, spinning at 1,350 RPM (50 Hz) or 1,550 RPM (60 Hz)"},
                {"Grind-by-Weight (GbW)", "Integrated scale inside the fork with 0.1 g accuracy"},
                {"Direct Mode", "on-demand grind"},
                {"Master Mode", "automatically detects portafilter and dose"},
                {"Hopper Capacity", "~1.5 kg (3.3 lbs)"},
                {"Productivity", "~3.4 g/sec output"},
                {"Cooling Technology", "EVO automatic fan cooling to stabilize grind temperature"},
                {"Touchscreen Display", "Responsive IPS screen showing dose stats, burr wear, date/time—and supports single/double/triple/manual dosing"},
                {"Maintenance", "Removable PRO chamber allows easy cleaning without losing grind settings"},
                {"Dimensions & Weight", "230 × 615 × 270 mm (~9″×24″×10.6″); weight ~13 kg (28.6-29 lbs)"}
            }),
            ("F83 E PRO", "F83 E برو", "The F83 E PRO is a heavy-duty electronic grinder equipped with massive 83mm flat burrs and a powerful motor for lightning-fast grinding. As part of the PRO line, it features a detachable grinding chamber, allowing for deep cleaning or burr access without losing your grind setting. It includes a built-in cooling fan and a high-definition touchscreen to monitor performance, making it the perfect choice for the busiest specialty coffee bars.", "تُعد F83 E PRO مطحنة إلكترونية قوية مصممة للعمل الشاق، وهي مزودة بشفرات مسطحة ضخمة مقاس 83 ملم ومحرك قوي لطحن فائق السرعة. كجزء من سلسلةبرو، تتميز بـ حجرة طحن قابلة للفصل، مما يسمح بالتنظيف العميق أو الوصول للشفرات دون فقدان إعدادات الطحن. تتضمن مروحة تبريد مدمجة وشاشة لمس عالية الدقة لمراقبة الأداء، مما يجعلها الخيار الأمثل للمقاهي المزدحمة جداً.", 
            
            8000.00m, 2178.35m, new[] { "Glossy Black" }, new() {
                {"Burrs", "83 mm flat M340 stainless steel burrs with Dark-T® titanium-Al-C-N coating"},
                {"Motor & Speed", "650 W motor spinning at approximately 1,350 RPM (50 Hz) or 1,550 RPM (60 Hz)"},
                {"Adjustment", "Stepless micrometric ring-nut grind adjustment"},
                {"Dosing", "On-demand touchscreen for single/double/triple/manual doses, with real-time"},
                {"Cooling Technology", "EVO automatic fan cooling to stabilize grind temperature"},
                {"Hopper Capacity", "1.5 kg (3.3 lbs) bean hopper"},
                {"Weight & Dimensions", "15 kg (33 lbs); ~230 × 670 × 270 mm (9 × 26.3 × 10.6 in)"},
                {"Maintenance", "Removable PRO chamber allows easy cleaning without losing grind settings"},
                {"Throughput", "Designed for high-volume cafés—up to ~300 drinks/day or ~7 kg coffee per day"},
                {"Dimensions", "230x670x270 Mm"}
            }),
            ("F83 E", "F83 E", "The F83 E is a heavy-duty professional electronic grinder featuring massive 83mm flat steel burrs. These larger burrs allow for much faster grinding speeds and improved heat dissipation, making it ideal for the busiest coffee environments. It features a large color touchscreen, micrometric adjustment, and a powerful motor designed to deliver precise doses in seconds, maintaining peak performance under heavy daily stress.", "تُعتبر F83 E الأخ الأكبر لمطحنة F64، وهي مصممة للمقاهي ذات الاستهلاك الكثيف جداً التي تتطلب سرعة وقوة عالية. تتميز بشفرات مسطحة ضخمة مقاس 83 ملم، مما يسمح بسرعة طحن فائقة وتوزيع أفضل للحرارة. تشتمل على شاشة ملونة كبيرة تعمل باللمس، ونظام تعديل ميكرومتري، ومحرك قوي مصمم لتقديم جرعات دقيقة في ثوانٍ معدودة، مع الحفاظ على الأداء العالي تحت ضغط العمل المستمر.", 
            
            6000.00m, 1633.76m, new[] { "White", "Matt Black, Red" }, new() {
                {"Ring nut micrometric grinding adjustment", "continuous"},
                {"Varnishing", "standard"},
                {"Doses adjustment", "in seconds"},
                {"Power", "650 Watt"},
                {"Burrs diameter", "Ø 83 mm / Burrs type: Flat"},
                {"Burrs revs", "1350/Min (50 Hz) – 1550/Min (60 Hz)"},
                {"Coffee bean hopper capacity", "1.5 Kg"},
                {"Net weight", "15 Kg"},
                {"Dimensions", "230x670x270 mm"},
                {"Recommended consumption", "Up to 7 kg per day"},
                {"Burrs life", "600 kg"}
            }),
            ("F64 Evo Pro", "F64 إيفو برو", "The F64 EVO PRO is a professional electronic grinder that features 64mm flat steel burrs and a built-in automatic cooling fan to maintain bean temperature during peak hours. The PRO designation signifies the Detachable Grinding Chamber; this innovative system allows baristas to remove the upper burr assembly for thorough cleaning or replacement without losing the current grind setting. It features a high-definition CapSense touchscreen that displays dose statistics, humidity, and temperature, making it one of the most reliable and user-friendly grinders on the market.", "تُعد مطحنة F64 EVO PRO الخيار الاحترافي الأمثل للمقاهي ذات الإقبال الكثيف، حيث تجمع بين سرعة سلسلة EVO وتصميم يسهل عملية الصيانة. تتميز بشفرات مسطحة مقاس 64 ملم ومروحة تبريد تلقائية للحفاظ على جودة البن خلال ساعات الذروة. يشير مسمى PRO إلى حجرة الطحن القابلة للفصل؛ هذا النظام المبتكر يسمح للمستخدم بفك الجزء العلوي من الشفرات للتنظيف العميق أو الاستبدال دون التأثير على درجة الطحن المحددة مسبقاً. كما تشتمل على شاشة CapSense عالية الدقة تعرض إحصائيات الجرعات، ونسبة الرطوبة، ودرجة الحرارة، مما يجعلها واحدة من أكثر المطاحن موثوقية وسهولة في الاستخدام.", 
            
            6000.00m, 1633.76m, new[] { "Glossy Black" }, new() {
                {"Ring nut micrometric grinding adjustment", "continuous"},
                {"Varnishing", "standard"},
                {"Doses adjustment", "in seconds"},
                {"Power", "350 Watt"},
                {"Burrs diameter", "Ø 64 Mm / Burrs type: flat"},
                {"Burrs Speed", "1350/Min (50 Hz) – 1550/Min (60 Hz)"},
                {"Coffee bean hopper capacity", "1.5 Kg"},
                {"Net weight", "13 Kg"},
                {"Dimensions", "230x615x270 Mm"}
            }),
            ("F64 Evo", "F64 إيفو", "The F64 EVO is an electronic grinder equipped with 64mm flat burrs and a built-in cooling fan that activates automatically to prevent beans from overheating. It features a fast, intuitive CapSense touchscreen. The F64 EVO PRO version features a Detachable Grinding Chamber, allowing baristas to remove the burrs for cleaning without losing their grind setting—a massive time-saver for busy coffee shops.", "تُعد F64 EVO مطحنة إلكترونية أسطورية مزودة بشفرات مسطحة مقاس 64 ملم ومروحة تبريد مدمجة تعمل تلقائياً لمنع ارتفاع حرارة البن. وتتميز بشاشة CapSense سريعة تعمل باللمس. أما إصدار F64 EVO PRO، فيتميز بـ حجرة طحن قابلة للفصل، مما يسمح بفك الشفرات للتنظيف دون فقدان إعدادات الطحن السابقة، وهو ما يوفر وقتاً كبيراً للمقاهي المزدحمة.", 
            
            5200.00m, 1415.93m, new[] { "Matt Black", "Glossy Black" }, new() {
                {"Ring nut micrometric grinding adjustment", "continuous"},
                {"Varnishing", "standard"},
                {"Doses adjustment", "in seconds"},
                {"Power", "450 Watt"},
                {"Burrs diameter", "Ø 64 Mm / Burrs type: Flat"},
                {"Burrs Speed", "1350/Min (50 Hz) – 1550/Min (60 Hz)"},
                {"Coffee bean hopper capacity", "1.5 kg"},
                {"Net weight", "13 Kg"},
                {"Dimensions", "230x615x270 mm"},
                {"Recommended consumption", "Up to 3 kg per day"}
            }),
            ("ALLGROUND Sense", "ALLGROUND سينس", "The AllGround Sense is a premium Grind-by-Weight grinder that features a high-precision scale built into the portafilter fork. This allows users to set a target weight (in grams) rather than time, ensuring absolute consistency for every shot. It retains the 64mm Dark-T coated burrs and the iconic color-coded touchscreen for Espresso, Moka, and Filter, but adds the luxury of real-time dose weighing to eliminate the need for external scales.", "تُعد AllGround Sense النسخة الأكثر تطوراً في سلسلة AllGround، حيث تضيف تقنية الطحن بالوزن عبر ميزان عالي الدقة مدمج في حامل البورتافلتر. يتيح ذلك للمستخدم تحديد وزن محدد (بالجرامات) بدلاً من الوقت، مما يضمن دقة متناهية في كل جرعة. تحتفظ المطحنة بشفرات Dark-T مقاس 64 ملم وشاشة اللمس الملونة (للإسبريسو، والموكا، والفلتر)، لكنها تضيف ميزة وزن الجرعة في الوقت الفعلي لتغنيك عن استخدام الموازين الخارجية.", 
            3560.00m, 969.37m, new[] { "Matt Black", "White" }, new() {
                {"Burrs", "64 mm flat Dark-T® coated steel burrs—titaniumaluminum-carbon-nitrogen blend"},
                {"Motor & Speed", "250 W; spinning at ~1,400 RPM (50 Hz) and ~1,600 RPM (60 Hz)"},
                {"Integrated Scale (Grind-by-Weight)", "Load-cell inside the fork, accurate dosing in real time (grams); touch display color-changes to show Espresso/Moka/Filter modes"},
                {"Adjustments", "Continuous micrometric ring-nut grind adjustment at ~10 µm per step"},
                {"Hopper Capacity", "250 g (.5 lbs) capacity"},
                {"Throughput & Retention", "Great for home use up to ~1 kg/day"},
                {"Size & Weight", "169 × 240 × 460 mm (6.6″ × 9.4″ × 18.1″); ≈9 kg (20 lbs)"},
                {"Display & Modes", "IPS touchscreen; color-coded for each mode with icons and timed dosing"},
                {"Maintenance", "Tool-free removable top burr chamber and collar makes cleaning"}
            }),
            ("ALLGROUND", "ALLGROUND", "The AllGround is a versatile, high-tech grinder that allows users to switch easily between Espresso, Moka, and Filter settings using a rotating ring nut. It features 64mm flat burrs with a specialized Dark-T (Titanium/Aluminum) coating, which lasts up to 5 times longer than standard burrs. The intuitive touchscreen display changes color based on the selected mode (Red for Espresso, Blue for Moka, Green for Filter), making it incredibly user-friendly.", "تُعتبر AllGround المطحنة الشاملة والأكثر تنوعاً، حيث تتيح للمستخدمين التنقل بسهولة بين إعدادات الإسبريسو، والموكا، والفلتر عبر حلقة دوارة. تتميز بشفرات مسطحة مقاس 64 ملم مع طلاء Dark-T المتطور (تيتانيوم وألمنيوم)، والذي يدوم لفترة أطول بـ 5 مرات من الشفرات العادية. تتغير ألوان شاشة اللمس بناءً على الوضع المختار (الأحمر للإسبريسو، الأزرق للموكا، الأخضر للفلتر)، مما يجعلها سهلة الاستخدام للغاية.", 
            2950.00m, 803.27m, new[] { "Matt Black" }, new() {
                {"Ring nut micrometric grinding adjustment", "continuous"},
                {"Doses adjustment", "in seconds"},
                {"Power", "250 Watt"},
                {"Burrs type", "Flat with Titanium coating"},
                {"Burrs diameter", "Ø 64 mm"},
                {"Burrs Speed", "1400/min (50 Hz) – 1600/min (60 Hz)"},
                {"coffee bean hopper capacity", "250g"},
                {"Net weight", "9 Kg"},
                {"Dimensions", "169x440x240 mm"},
                {"Recommended consumption", "Up to 1 kg per day"}
            }),
            ("F4 Filter", "F4 فلتر", "The F4 Filter is a specialized electronic grinder tailored for filter coffee enthusiasts. Unlike standard espresso models, it features a dedicated ground coffee container (100g) and an intuitive LCD touchscreen. It allows for 3 programmable dose settings and features 64mm flat burrs designed to produce a uniform grind size with fewer fines, ensuring a clean and aromatic cup of filter coffee.", "مطحنة F4 Filter هي نسخة إلكترونية متخصصة مصممة لعشاق قهوة الفلتر (الترشيح). بخلاف موديلات الإسبريسو، تأتي هذه المطحنة مع حاوية بن مطحون مخصصة (100 جرام) وشاشة LCD تعمل باللمس. تسمح ببرمجة 3 إعدادات للجرعات، وتستخدم شفرات مسطحة مقاس 64 ملم مصممة لإنتاج طحنة متناسقة تضمن كوباً صافياً وعطرياً من القهوة المقطرة.", 
            
            2400.00m, 653.51m, new[] { "Red" }, new() {
                {"Ring nut micrometric grinding adjustment", "continuous"},
                {"Doses adjustment", "in seconds – 3 memory programs"},
                {"Power", "250 Watt"},
                {"Burrs type", "Flat"},
                {"Burrs diameter", "Ø 64 mm"},
                {"Burrs Speed", "1350 /min (50 Hz) – 1550/min (60 Hz)"},
                {"coffee bean hopper capacity", "250g"},
                {"Ground coffee container capacity", "100 g"},
                {"Net weight", "8 Kg"},
                {"Dimensions", "169x473x240 mm"},
                {"Recommended consumption", "Up to 1 kg per day"}
            }),
            ("F5 A", "F5 A", "The F5 A is a professional-grade automatic grinder featuring large 64mm flat steel burrs and a high-capacity 1.5kg hopper. The stands for Automatic, meaning the grinder uses a microswitch to sense when the doser is low and automatically grinds more coffee to keep it full. It is known for its durability, consistency, and a powerful 350W motor that can handle constant daily use without overheating.", "تُعد F5 A مطحنة إسبريسو احترافية مصممة للمقاهي ذات الإقبال العالي. تتميز بشفرات مسطحة كبيرة مقاس 64 ملم وخزان حبوب بسعة 1.5 كجم. يشير حرف إلى أنها أوتوماتيكية، حيث تستخدم مفتاحاً دقيقاً يستشعر انخفاض مستوى القهوة في الموزع ويقوم بالطحن تلقائياً لإبقائه ممتلئاً. تشتهر بمتانتها، وتناسق طحنها، ومحركها القوي بقدرة 350 واط الذي يتحمل الاستخدام المستمر دون ارتفاع الحرارة.", 
            3000.00m, 816.88m, new[] { "Matt Black", "White", "Red" }, new() {
                {"Ring nut micrometric grinding adjustment", "continuous"},
                {"Power", "350 watt"},
                {"Burrs diameter", "Ø 64 mm / Burrs type: flat"},
                {"Burrs Speed", "1350/min (50 Hz) - 1550/min (60 Hz)"},
                {"Dispenser unit adjustment for shots", "10 - 5.5 g"},
                {"Coffee bean hopper capacity", "1.5 kg"},
                {"Doser capacity", "250 g"},
                {"Net weight", "14 kg"},
                {"Dimensions", "230x615x270 mm"},
                {"Recommended consumption", "Up to 2 kg per day"}
            }),
            ("F4 A", "F4 A", "The Fiorenzato F4 A is a compact professional doser grinder featuring 58mm flat steel burrs. The model indicates it is Automatic, meaning it has a microswitch that automatically refills the doser chamber as coffee is used. It is designed for durability and space-saving, offering stepless adjustment to fine-tune the grind for a perfect espresso shot.", "تُعد فيورينزاتو F4 A مطحنة احترافية مدمجة بنظام الدوسر (الموزع)، مزودة بشفرات مسطحة مقاس 58 ملم. يشير حرف  إلى أنها أوتوماتيكية، حيث تحتوي على مفتاح دقيق يقوم بإعادة ملء حجرة التوزيع تلقائياً عند استخدام القهوة. صُممت لتكون متينة وموفرة للمساحة، مع نظام تعديل دقيق جداً (Stepless) لضبط الطحنة للحصول على كوب إسبريسو مثالي.", 
            2400.00m, 653.51m, new[] { "Matt Black", "White", "Red" }, new() {
                {"Ring nut micrometric grinding adjustment", "continuous"},
                {"Varnishing", "standard"},
                {"Fork", "standard"},
                {"Power", "250 watt"},
                {"Burrs diameter", "Ø 58 mm / Burrs type: flat"},
                {"Burrs Speed", "1400/min (50 Hz) - 1600/min (60 Hz)"},
                {"Dispenser unit adjustment for shots", "10 - 5.5 g"},
                {"Coffee bean hopper capacity", "500 g"},
                {"Doser capacity", "200 g"},
                {"Net weight", "10 kg"},
                {"Dimensions", "169x473x240 mm"},
                {"Recommended consumption", "Up to 1 kg per day"}
            }),
            ("F5 D", "F5 D", "The Fiorenzato F5 D is a heavy-duty professional shop grinder equipped with 64mm flat steel burrs. Unlike espresso grinders that dose into a portafilter, the D (Droggheria) model features a bag holder clip, making it perfect for coffee shops that sell pre-ground beans to customers. It features a continuous micrometric adjustment ring for high precision and a powerful 350W motor that prevents overheating during high-volume use.", "مطحنة فيورينزاتو F5 (المعروفة بـ Droggheria) هي مطحنة احترافية مخصصة للمتاجر، مزودة بشفرات فولاذية مسطحة مقاس 64 ملم. على عكس مطاحن الإسبريسو التقليدية، يتميز موديل  بمشبك لتثبيت الأكياس، مما يجعلها مثالية للمقاهي التي تبيع البن المطحون للزبائن. تحتوي على حلقة تعديل ميكرومترية مستمرة لدقة عالية، ومحرك قوي بقدرة 350 واط يمنع ارتفاع حرارة البن أثناء الطحن بكميات كبيرة.", 
            
            2700.00m, 735.19m, new[] { "Matt Black", "White" }, new() {
                {"Burrs", "64 mm flat steel burrs"},
                {"Motor", "350 W; approximately 1,350 RPM at 50 Hz / 1,550 RPM at 60 Hz"},
                {"Bean Hopper", "Holds about 1.5 kg of beans"},
                {"Doser Capacity", "Built-in doser for measured portion delivery; bag holder/clamp included"},
                {"Dimensions", "230 × 615 × 270 mm (9″ × 24″ × 10.6″)"},
                {"Weight", "Around 13 kg"},
                {"Construction", "Solid die-cast metal body; low heat generation to protect aroma and flavour"},
                {"Throughput", "Recommended for low-to-medium volume (up to ~2 kg coffee/day)"}
            }),
            ("PIETRO", "بيترو", "The Pietro is a high-end manual grinder featuring 58mm vertical flat burrs made of M390 steel. It is designed with a unique retractable lever and an airtight grounds container to preserve aroma. It comes in two versions: B-Modal (all-purpose for espresso and filter) and M-Modal (optimized for high-clarity filter brewing). Its ergonomic, non-slip base makes it a standout for home enthusiasts who want electric-quality grinding without a motor.", "تُعد بيترو مطحنة يدوية فاخرة من فيورينزاتو، وهي أول مطحنة يدوية تستخدم شفرات مسطحة عمودية مقاس 58 ملم مصنوعة من فولاذ M390. تتميز بتصميم مبتكر يشمل ذراعاً قابلاً للطي وحاوية بن محكمة الإغلاق للحفاظ على النكهة. تتوفر بإصدارين: B-Modal (شامل للإسبريسو والفلتر) و M-Modal (مخصص لقهوة الفلتر). بفضل قاعدتها غير القابلة للانزلاق وتصميمها المريح، توفر جودة طحن تضاهي المطاحن الكهربائية.", 1575.00m, 428.86m, new[] { "Matt Black", "White" }, new() {
                {"Type of grind", "Espresso - Moka - Filter"},
                {"Max capacity loading hopper", "60g"},
                {"Max capacity ground coffee container", "70g"},
                {"Size", "225 x 115 x 80 mm"},
                {"Weight", "1600 g"},
                {"Burrs Diameter", "Ø 58 mm"}
            })
        };

        var newProducts = new List<Product>();

        foreach (var g in grinders)
        {
            // Explicitly create a new Dictionary to ensure EF Core properly persists specifications
            var specifications = new Dictionary<string, string>(g.Specs);

            var product = new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = category.Id,
                BrandId = brand.Id,
                Slug = g.Name.ToLower().Replace(" ", "-").Replace("(", "").Replace(")", "").Replace(",", "").Replace("/", "-").Replace("®", "").Replace(":", ""),
                IsActive = true,
                Translations = new List<ProductTranslation>
                {
                    new() { LanguageCode = "en", Name = g.Name, Description = g.Description },
                    new() { LanguageCode = "ar", Name = g.NameAr, Description = g.DescriptionAr }
                },
                EquipmentProfile = new ProductEquipmentProfile
                {
                    Specifications = specifications
                }
            };

            var variants = new List<ProductVariant>();
            foreach (var col in g.Colors)
            {
                var colorVal = await GetOrAddColorFallback(col);
                var variant = new ProductVariant
                {
                    Id = Guid.NewGuid(),
                    Sku = $"{product.Slug}-{col.Replace(" ", "")}".ToUpper(),
                    Price = g.PriceAED,
                    StockQuantity = 10,
                    Options = new List<ProductVariantOption>
                    {
                        new() { ProductAttributeValueId = colorVal.Id }
                    },
                    Prices = new List<ProductVariantPrice>
                    {
                        new() { Currency = Currency.USD, Price = g.PriceUSD },
                        new() { Currency = Currency.AED, Price = g.PriceAED }
                    }
                };
                variants.Add(variant);
            }
            product.Variants = variants;
            newProducts.Add(product);
        }

        await context.Products.AddRangeAsync(newProducts);
        await TrySaveAsync(context, logger, "FiorenzatoGrinders");
        logger.LogInformation("Fiorenzato Grinders seeded successfully.");
    }
}
