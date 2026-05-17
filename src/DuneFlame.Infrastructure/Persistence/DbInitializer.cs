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
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogError("Concurrency exception on {EntityLabel}", entityLabel);
            foreach (var entry in ex.Entries)
            {
                var modifiedProps = entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name);
                logger.LogError("Entity: {EntityName}, State: {State}, Id: {Id}, Modified Properties: {Props}", entry.Entity.GetType().Name, entry.State, entry.Property("Id").CurrentValue, string.Join(", ", modifiedProps));
            }
            throw;
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

            // 7. Seed Shipping Data (Countries, Cities, Rates)
            await SeedShippingDataAsync(context, logger);

            // 8. Seed / backfill master-data translations (Origins, RoastLevels, GrindTypes, FlavourNotes)
            await SeedMasterDataTranslationsAsync(context, logger);

            logger.LogInformation("Database initialization completed successfully.");
            logger.LogInformation("✓ Multi-Currency Support: ProductPrices seeded for USD, AED");
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

    private static async Task SeedMasterDataTranslationsAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        logger.LogInformation("Seeding master-data translations (Origins, RoastLevels, GrindTypes, FlavourNotes backfill)...");

        // --- Origins ---
        var originArNames = new Dictionary<string, string>
        {
            ["Ethiopia"]   = "إثيوبيا",
            ["Colombia"]   = "كولومبيا",
            ["Brazil"]     = "البرازيل",
            ["Kenya"]      = "كينيا",
            ["Costa Rica"] = "كوستاريكا",
            ["Malaysia"]   = "ماليزيا"
        };

        var origins = await context.Origins.Include(o => o.Translations).ToListAsync();
        foreach (var origin in origins)
        {
            if (!origin.Translations.Any(t => t.LanguageCode == "en"))
            {
                var t = new OriginTranslation { LanguageCode = "en", TranslatedName = origin.Name };
                origin.Translations.Add(t);
                context.Add(t);
            }
            if (originArNames.TryGetValue(origin.Name, out var arName) && !origin.Translations.Any(t => t.LanguageCode == "ar"))
            {
                var t = new OriginTranslation { LanguageCode = "ar", TranslatedName = arName };
                origin.Translations.Add(t);
                context.Add(t);
            }
        }

        // --- Roast Levels ---
        var roastArNames = new Dictionary<string, string>
        {
            ["Light"]        = "تحميص خفيف",
            ["Medium"]       = "تحميص متوسط",
            ["Medium-Dark"]  = "تحميص متوسط غامق",
            ["Dark"]         = "تحميص غامق"
        };

        var roastLevels = await context.Set<RoastLevelEntity>().Include(r => r.Translations).ToListAsync();
        foreach (var roast in roastLevels)
        {
            if (!roast.Translations.Any(t => t.LanguageCode == "en"))
            {
                var t = new RoastLevelTranslation { LanguageCode = "en", TranslatedName = roast.Name };
                roast.Translations.Add(t);
                context.Add(t);
            }
            if (roastArNames.TryGetValue(roast.Name, out var arName) && !roast.Translations.Any(t => t.LanguageCode == "ar"))
            {
                var t = new RoastLevelTranslation { LanguageCode = "ar", TranslatedName = arName };
                roast.Translations.Add(t);
                context.Add(t);
            }
        }

        // --- Grind Types ---
        var grindArNames = new Dictionary<string, string>
        {
            ["Whole Bean"]    = "حبوب كاملة",
            ["Espresso"]      = "إسبريسو",
            ["Filter"]        = "فلتر",
            ["Moka"]          = "موكا",
            ["French Press"]  = "فرنش بريس",
            ["Cold Brew"]     = "كولد برو"
        };

        var grindTypes = await context.Set<GrindType>().Include(g => g.Translations).ToListAsync();
        foreach (var grind in grindTypes)
        {
            if (!grind.Translations.Any(t => t.LanguageCode == "en"))
            {
                var t = new GrindTypeTranslation { LanguageCode = "en", TranslatedName = grind.Name };
                grind.Translations.Add(t);
                context.Add(t);
            }
            if (grindArNames.TryGetValue(grind.Name, out var arName) && !grind.Translations.Any(t => t.LanguageCode == "ar"))
            {
                var t = new GrindTypeTranslation { LanguageCode = "ar", TranslatedName = arName };
                grind.Translations.Add(t);
                context.Add(t);
            }
        }

        // --- Brands ---
        var brandArNames = new Dictionary<string, string>
        {
            ["DuneFlame"]    = "دون فليم",
            ["Fiorenzato"]   = "فيورينزاتو",
            ["MHW-3BOMBER"]  = "إم إتش دبليو-٣بومبر"
        };
        var brandArDescriptions = new Dictionary<string, string>
        {
            ["DuneFlame"]   = "تشكيلة مميزة من القهوة المحمصة طازجاً",
            ["Fiorenzato"]  = "مطاحن قهوة احترافية",
            ["MHW-3BOMBER"] = "ملحقات ومعدات قهوة احترافية"
        };

        var brands = await context.Brands.Include(b => b.Translations).ToListAsync();
        foreach (var brand in brands)
        {
            if (!brand.Translations.Any(t => t.LanguageCode == "en"))
            {
                var t = new BrandTranslation { LanguageCode = "en", TranslatedName = brand.Name, TranslatedDescription = brand.Description };
                brand.Translations.Add(t);
                context.Add(t);
            }
            if (brandArNames.TryGetValue(brand.Name, out var arBrandName) && !brand.Translations.Any(t => t.LanguageCode == "ar"))
            {
                var t = new BrandTranslation { LanguageCode = "ar", TranslatedName = arBrandName, TranslatedDescription = brandArDescriptions.GetValueOrDefault(brand.Name) };
                brand.Translations.Add(t);
                context.Add(t);
            }
        }

        // --- ProductAttributes ---
        var attributeArNames = new Dictionary<string, string>
        {
            ["Weight"] = "الوزن",
            ["Color"]  = "اللون"
        };

        var attributes = await context.ProductAttributes.Include(a => a.Translations).ToListAsync();
        foreach (var attr in attributes)
        {
            if (!attr.Translations.Any(t => t.LanguageCode == "en"))
            {
                var t = new ProductAttributeTranslation { LanguageCode = "en", TranslatedName = attr.Name };
                attr.Translations.Add(t);
                context.Add(t);
            }
            if (attributeArNames.TryGetValue(attr.Name, out var arAttrName) && !attr.Translations.Any(t => t.LanguageCode == "ar"))
            {
                var t = new ProductAttributeTranslation { LanguageCode = "ar", TranslatedName = arAttrName };
                attr.Translations.Add(t);
                context.Add(t);
            }
        }

        // --- ProductAttributeValues ---
        var attributeValueArNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Weight
            ["250g"]            = "٢٥٠ غرام",
            ["1kg"]             = "١ كيلوغرام",
            // Colors
            ["Black"]           = "أسود",
            ["White"]           = "أبيض",
            ["Red"]             = "أحمر",
            ["Glossy"]          = "لامع",
            ["Glossy Black"]    = "أسود لامع",
            ["Matte Black"]     = "أسود غير لامع",
            ["Matt Black"]      = "أسود غير لامع",   // Fiorenzato seed uses "Matt Black" (no 'e')
            ["Silver"]          = "فضي",
            ["Silver Spot"]     = "نقاط فضية",
            ["Multicolor"]      = "متعدد الألوان",
            ["Transparent"]     = "شفاف",
            ["Walnut"]          = "خشب الجوز",
            ["Dark grey"]       = "رمادي داكن",
            ["Space Silver"]    = "فضي فضائي",
            ["Obsidian Black"]  = "أسود أوبسيدياني",
            ["Metal Color"]     = "لون معدني",
            ["Matt Black, Red"] = "أسود غير لامع وأحمر",
            ["Silicone"]        = "سيليكون",
        };

        var attrValues = await context.ProductAttributeValues.Include(v => v.Translations).ToListAsync();
        foreach (var val in attrValues)
        {
            if (!val.Translations.Any(t => t.LanguageCode == "en"))
            {
                var t = new ProductAttributeValueTranslation { LanguageCode = "en", TranslatedValue = val.Value };
                val.Translations.Add(t);
                context.Add(t);
            }
            if (attributeValueArNames.TryGetValue(val.Value, out var arVal) && !val.Translations.Any(t => t.LanguageCode == "ar"))
            {
                var t = new ProductAttributeValueTranslation { LanguageCode = "ar", TranslatedValue = arVal };
                val.Translations.Add(t);
                context.Add(t);
            }
        }

        // --- FlavourNote Arabic backfill (for rows already in DB from old seed) ---
        var flavourNoteArNames = new Dictionary<string, string>
        {
            ["Chocolate"]      = "شوكولاتة",
            ["Walnut"]         = "جوز",
            ["Cookies"]        = "بسكويت",
            ["Jasmine"]        = "ياسمين",
            ["Floral"]         = "زهري",
            ["Peach"]          = "خوخ",
            ["Bergamot"]       = "برغموت",
            ["Orange Blossom"] = "زهر البرتقال",
            ["Mandarin"]       = "يوسفي",
            ["Tropical Fruit"] = "فاكهة استوائية",
            ["Sweet Tobacco"]  = "تبغ حلو",
            ["Dark Caramel"]   = "كراميل داكن",
            ["Woody Spice"]    = "توابل خشبية"
        };

        var flavourNotes = await context.Set<FlavourNote>().Include(f => f.Translations).ToListAsync();
        foreach (var note in flavourNotes)
        {
            if (!note.Translations.Any(t => t.LanguageCode == "en"))
            {
                var t = new FlavourNoteTranslation { LanguageCode = "en", Name = note.Name };
                note.Translations.Add(t);
                context.Add(t);
            }
            if (flavourNoteArNames.TryGetValue(note.Name, out var arName) && !note.Translations.Any(t => t.LanguageCode == "ar"))
            {
                var t = new FlavourNoteTranslation { LanguageCode = "ar", Name = arName };
                note.Translations.Add(t);
                context.Add(t);
            }
        }

        await TrySaveAsync(context, logger, "MasterDataTranslations");
        logger.LogInformation("Master-data translations seeding completed.");
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
            // Migrate old category names → new hierarchy names (one-time, idempotent).
            await MigrateCategoryTreeAsync(context, logger);
            // Apply DisplayOrder to existing categories (idempotent).
            await SeedCategoryDisplayOrdersAsync(context, logger);
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

        // Flat tree definition: (Slug, NameEn, NameAr, IsCoffee, ParentSlug, DisplayOrder)
        // Ordered top-down — each entry's ParentSlug is always resolved before we reach it.
        var treeDefinition = new (string Slug, string NameEn, string NameAr, bool IsCoffee, string ParentSlug, int DisplayOrder)[]
        {
            // WAVE 1 — L1 children of root
            ("coffee",               "Coffee",                 "قهوة",               true,  "root",         1),
            ("equipment",            "Equipment",              "معدات",              false, "root",         2),

            // WAVE 2a — L2 children of coffee (Beans=1, Capsules=2, Drip Bags=3)
            ("beans",                "Beans",                  "حبوب",               true,  "coffee",       1),
            ("capsules",             "Capsules",               "كبسولات",            true,  "coffee",       2),
            ("drip-bags",            "Drip Bags",              "أكياس تقطير",        true,  "coffee",       3),

            // WAVE 2b — L2 children of equipment
            ("coffee-machines",      "Coffee Machines",        "آلات القهوة",        false, "equipment",    1),
            ("grinders",             "Grinders",               "مطاحن",              false, "equipment",    2),
            ("brewing",              "Brewing",                "تحضير",              false, "equipment",    3),
            ("accessories",          "Accessories",            "ملحقات",             false, "equipment",    4),
            ("cleaning-maintenance", "Cleaning & Maintenance", "التنظيف والصيانة",   false, "equipment",    5),

            // WAVE 3a — L3 children of coffee-machines
            ("home-machines",         "Home Machines",         "آلات منزلية",        false, "coffee-machines", 1),
            ("professional-machines", "Professional Machines", "آلات احترافية",      false, "coffee-machines", 2),

            // WAVE 3b — L3 children of grinders
            ("manual",               "Manual",                 "يدوية",              false, "grinders",     1),
            ("electric",             "Electric",               "كهربائية",           false, "grinders",     2),
        };

        // BFS wave order — each entry is a parent slug whose children form one committed batch.
        // Parents must be committed before children reference them as FKs.
        var waveOrder = new[] { "root", "coffee", "equipment", "coffee-machines", "grinders" };

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
                    DisplayOrder = node.DisplayOrder,
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

    /// <summary>
    /// One-time, idempotent migration that renames old category translations to the
    /// new hierarchy names. Safe to call on every startup — if the old names are
    /// already gone, it simply does nothing.
    /// </summary>
    private static async Task MigrateCategoryTreeAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        // Map of (OldEnglishName → NewEnglishName).  Arabic names stay the same because
        // we did not change them in the tree definition.
        var renames = new Dictionary<string, string>
        {
            ["Brewing Equipment"]   = "Brewing",
            ["Drinkware & Storage"] = "Accessories",
            ["Cleaning Maintenance"]= "Cleaning & Maintenance",
        };

        var changed = false;
        foreach (var (oldName, newName) in renames)
        {
            var translation = await context.Set<CategoryTranslation>()
                .FirstOrDefaultAsync(t => t.LanguageCode == "en" && t.Name == oldName);

            if (translation != null)
            {
                logger.LogInformation("Migrating category translation '{Old}' → '{New}'", oldName, newName);
                translation.Name = newName;
                changed = true;
            }
        }

        // Also update slugs if the old slug convention differed
        var slugRenames = new Dictionary<string, string>
        {
            ["brewing-equipment"]   = "brewing",
            ["drinkware-storage"]   = "accessories",
            ["professional-coffee-grinders"] = "electric"
            // "cleaning-maintenance" slug was already correct
        };

        foreach (var (oldSlug, newSlug) in slugRenames)
        {
            var category = await context.Categories
                .FirstOrDefaultAsync(c => c.Slug == oldSlug);

            if (category != null)
            {
                logger.LogInformation("Migrating category slug '{Old}' → '{New}'", oldSlug, newSlug);
                category.Slug = newSlug;
                changed = true;
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("Category tree migration completed.");
        }
        else
        {
            logger.LogInformation("Category tree already up-to-date. No migration needed.");
        }
    }

    /// <summary>
    /// Idempotent: applies the canonical DisplayOrder values to existing categories.
    /// Safe to call on every startup — only writes when a value differs.
    /// </summary>
    private static async Task SeedCategoryDisplayOrdersAsync(AppDbContext context, ILogger<AppDbContext> logger)
    {
        // slug → desired DisplayOrder
        var displayOrders = new Dictionary<string, int>
        {
            // L2 Coffee children
            ["beans"]                = 1,
            ["capsules"]             = 2,
            ["drip-bags"]            = 3,
            // L2 Equipment children
            ["coffee-machines"]      = 1,
            ["grinders"]             = 2,
            ["brewing"]              = 3,
            ["accessories"]          = 4,
            ["cleaning-maintenance"] = 5,
        };

        var changed = false;
        foreach (var (slug, order) in displayOrders)
        {
            var category = await context.Categories.FirstOrDefaultAsync(c => c.Slug == slug);
            if (category != null && category.DisplayOrder != order)
            {
                logger.LogInformation("Updating DisplayOrder for '{Slug}': {Old} → {New}", slug, category.DisplayOrder, order);
                category.DisplayOrder = order;
                changed = true;
            }
        }

        if (changed)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("Category DisplayOrder seeding completed.");
        }
        else
        {
            logger.LogInformation("Category DisplayOrders already up-to-date.");
        }
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
        var coffeeBeansCategory = await context.Categories.FirstOrDefaultAsync(c => c.Slug == "beans")
            ?? throw new InvalidOperationException("Seeding prerequisite missing: Category with slug 'beans' was not found. Ensure SeedCategoriesAsync completed successfully before SeedProductsAsync.");

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
        lightRoast = await context.Set<RoastLevelEntity>().FirstOrDefaultAsync(r => r.Name == "Light") ?? lightRoast;
        wholeBean = await context.Set<GrindType>().FirstOrDefaultAsync(g => g.Name == "Whole Bean") ?? wholeBean;

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
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Chocolate" },
                                new() { LanguageCode = "ar", Name = "شوكولاتة" }
                            }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 2,
                            Name = "Walnut",
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Walnut" },
                                new() { LanguageCode = "ar", Name = "جوز" }
                            }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 3,
                            Name = "Cookies",
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Cookies" },
                                new() { LanguageCode = "ar", Name = "بسكويت" }
                            }
                        }
                    }
                },
                Variants = new List<ProductVariant>
                {
                    new ProductVariant { Sku = "brazil-lenis-250g", Price = 46.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 46.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 12.51m } }, StockQuantity = 100, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight250g.Id } } },
                    new ProductVariant { Sku = "brazil-lenis-1kg", Price = 154.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 154.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 41.89m } }, StockQuantity = 100, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight1kg.Id } } }
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
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Jasmine" },
                                new() { LanguageCode = "ar", Name = "ياسمين" }
                            }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 2,
                            Name = "Floral",
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Floral" },
                                new() { LanguageCode = "ar", Name = "زهري" }
                            }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 3,
                            Name = "Peach",
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Peach" },
                                new() { LanguageCode = "ar", Name = "خوخ" }
                            }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 4,
                            Name = "Bergamot",
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Bergamot" },
                                new() { LanguageCode = "ar", Name = "برغموت" }
                            }
                        }
                    }
                },
                Variants = new List<ProductVariant>
                {
                    new ProductVariant { Sku = "ethiopia-guji-hambela-250g", Price = 53.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 53.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 14.42m } }, StockQuantity = 98, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight250g.Id } } },
                    new ProductVariant { Sku = "ethiopia-guji-hambela-1kg", Price = 179.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 179.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 48.69m } }, StockQuantity = 98, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight1kg.Id } } }
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
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Orange Blossom" },
                                new() { LanguageCode = "ar", Name = "زهر البرتقال" }
                            }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 2,
                            Name = "Mandarin",
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Mandarin" },
                                new() { LanguageCode = "ar", Name = "يوسفي" }
                            }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 3,
                            Name = "Tropical Fruit",
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Tropical Fruit" },
                                new() { LanguageCode = "ar", Name = "فاكهة استوائية" }
                            }
                        }
                    }
                },
                Variants = new List<ProductVariant>
                {
                    new ProductVariant { Sku = "tutti-frutti-250g", Price = 139.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 139.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 37.81m } }, StockQuantity = 98, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight250g.Id } } }
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
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Sweet Tobacco" },
                                new() { LanguageCode = "ar", Name = "تبغ حلو" }
                            }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 2,
                            Name = "Dark Caramel",
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Dark Caramel" },
                                new() { LanguageCode = "ar", Name = "كراميل داكن" }
                            }
                        },
                        new FlavourNote
                        {
                            DisplayOrder = 3,
                            Name = "Woody Spice",
                            Translations = new List<FlavourNoteTranslation>
                            {
                                new() { LanguageCode = "en", Name = "Woody Spice" },
                                new() { LanguageCode = "ar", Name = "توابل خشبية" }
                            }
                        }
                    }
                },
                Variants = new List<ProductVariant>
                {
                    new ProductVariant { Sku = "dokha-250g", Price = 139.00m, Prices = new List<ProductVariantPrice> { new ProductVariantPrice { Currency = Currency.AED, Price = 139.00m }, new ProductVariantPrice { Currency = Currency.USD, Price = 37.81m } }, StockQuantity = 100, Options = new List<ProductVariantOption> { new ProductVariantOption { ProductAttributeValueId = weight250g.Id } } }
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
            .FirstOrDefaultAsync(c => c.Translations.Any(t => t.LanguageCode == "en" && t.Name == "Brewing"))
            ?? throw new InvalidOperationException("Seeding prerequisite missing: Category 'Brewing' not found.");

        var drinkwareStorageCat = await context.Categories
            .FirstOrDefaultAsync(c => c.Translations.Any(t => t.LanguageCode == "en" && t.Name == "Accessories"))
            ?? throw new InvalidOperationException("Seeding prerequisite missing: Category 'Accessories' not found.");

        var cleaningMaintenanceCat = await context.Categories
            .FirstOrDefaultAsync(c => c.Translations.Any(t => t.LanguageCode == "en" && t.Name == "Cleaning & Maintenance"))
            ?? throw new InvalidOperationException("Seeding prerequisite missing: Category 'Cleaning & Maintenance' not found.");

        Category GetCategory(string name) => name switch
        {
            "Brewing" => brewingEquipmentCat,
            "Accessories" => drinkwareStorageCat,
            "Cleaning & Maintenance" => cleaningMaintenanceCat,
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

        // (NameEn, NameAr, DescriptionEn, DescriptionAr, CategoryName, Variants: (Color, AED, USD)[])
        // Color = "N/A" means no Color attribute — one default variant.
        var accessories = new (string NameEn, string NameAr, string DescriptionEn, string DescriptionAr, string CategoryName, (string Color, decimal AED, decimal USD)[] Variants)[]
        {
            ("MHW GT Milk Pitcher-400ml-U-shape spout", "إبريق حليب إم إتش دبليو جي تي سعة ٤٠٠ مل بفوهة على شكل حرف يو", "Elevate your latte art game with the MHW GT Milk Pitcher, featuring a precise 400ml capacity. This premium pitcher is crafted with a specially designed U-shape spout that guarantees optimal flow control for intricate pouring. The stunning glossy finish not only looks professional but also resists stubborn milk stains and scratches. Its ergonomic handle ensures a comfortable grip, allowing baristas of all levels to pour with confidence and stability. Whether you are a home enthusiast or a cafe professional, this pitcher is the perfect tool for creating flawless microfoam masterpieces.", "ارتقِ بمهاراتك في فن اللاتيه مع إبريق الحليب إم إتش دبليو جي تي، الذي يتميز بسعة دقيقة تبلغ ٤٠٠ مل. تم تصميم هذا الإبريق الفاخر بفوهة خاصة على شكل حرف يو تضمن التحكم الأمثل في التدفق للصب الدقيق. اللمسة النهائية اللامعة المذهلة لا تبدو احترافية فحسب، بل تقاوم أيضاً بقع الحليب العنيدة والخدوش. يضمن مقبضه المريح قبضة محكمة، مما يسمح للباريستا من جميع المستويات بالصب بثقة وثبات. سواء كنت من عشاق القهوة في المنزل أو محترفاً في مقهى، فإن هذا الإبريق هو الأداة المثالية لإنشاء روائع الرغوة الدقيقة الخالية من العيوب.", "Brewing", new[] { ("Glossy", 133m, 35.91m) }),
            ("MHW Milk pitcher 5.0-500ml", "إبريق حليب إم إتش دبليو ٥.٠ سعة ٥٠٠ مل", "Discover the ultimate versatility in milk frothing with the MHW Milk Pitcher 5.0, boasting a generous 500ml capacity. Available in stunning multicolor, classic glossy, and unique silver spot finishes, there is a style to match any espresso machine setup. The carefully engineered spout allows for smooth, consistent pouring, making it easier than ever to create detailed rosettas and tulips. Built from high-quality stainless steel, it provides excellent thermal conductivity so you can accurately feel the milk reaching the perfect temperature. Upgrade your daily coffee ritual with this durable, aesthetically pleasing, and highly functional frothing essential.", "اكتشف التنوع المطلق في تبخير الحليب مع إبريق الحليب إم إتش دبليو ٥.٠، الذي يتميز بسعة سخية تبلغ ٥٠٠ مل. يتوفر هذا الإبريق بألوان متعددة مذهلة، ولمعان كلاسيكي، وتصميم فريد بنقاط فضية، ليناسب إعداد أي آلة إسبريسو. تتيح الفوهة المصممة بعناية صباً سلساً ومتسقاً، مما يجعل إنشاء تصميمات الروزيتا والتوليب المفصلة أسهل من أي وقت مضى. مصنوع من الفولاذ المقاوم للصدأ عالي الجودة، ويوفر توصيلاً حرارياً ممتازاً لتتمكن من الشعور بدقة بوصول الحليب إلى درجة الحرارة المثالية. قم بترقية طقوس القهوة اليومية الخاصة بك مع هذه الأداة المتينة والجميلة والعملية للغاية والأساسية للتبخير.", "Brewing", new[] { ("Multicolor", 133m, 35.91m), ("Glossy", 109m, 29.43m), ("Silver Spot", 133m, 35.91m) }),
            ("MHW Turbo milk pitcher-450ml", "إبريق حليب إم إتش دبليو تيربو سعة ٤٥٠ مل", "The MHW Turbo Milk Pitcher is expertly designed for those who demand precision and speed in their milk steaming process. With a 450ml volume, it perfectly accommodates the ideal amount of milk for a standard latte or flat white. You can choose between the sleek glossy finish or the eye-catching silver spot design to beautifully complement your barista workstation. Its unique interior shape promotes a rapid vortex while steaming, ensuring silky, even microfoam every single time. The sharply tapered spout provides unparalleled control, allowing you to execute the most complex latte art designs effortlessly.", "تم تصميم إبريق الحليب إم إتش دبليو تيربو بخبرة لأولئك الذين يطلبون الدقة والسرعة في عملية تبخير الحليب. بسعة ٤٥٠ مل، يستوعب الإبريق الكمية المثالية من الحليب لتحضير اللاتيه أو الفلات وايت القياسي. يمكنك الاختيار بين اللمسة النهائية اللامعة الأنيقة أو التصميم الجذاب ذو النقاط الفضية ليكمل محطة الباريستا الخاصة بك بشكل جميل. يعزز شكله الداخلي الفريد دوامة سريعة أثناء التبخير، مما يضمن الحصول على رغوة دقيقة وحريرية ومتساوية في كل مرة. توفر الفوهة المدببة بشكل حاد تحكماً لا مثيل له، مما يتيح لك تنفيذ أعقد تصميمات فن اللاتيه دون عناء.", "Brewing", new[] { ("Glossy", 109m, 29.43m), ("Silver Spot", 133m, 35.91m) }),
            ("MHW OZ Cup-50ml", "كوب قياس إم إتش دبليو أوز سعة ٥٠ مل", "Accurately measure your espresso yields with the highly practical and durable MHW OZ Cup. This 50ml measuring glass is an indispensable tool for dialing in your espresso grinder and perfecting your daily extraction recipes. The clear, easy-to-read measurement markings ensure you know exactly how much liquid has been dispensed at a single glance. Made from premium, heat-resistant glass, it comfortably withstands the high temperatures of freshly brewed espresso without cracking or shattering. Its compact size fits perfectly under commercial and home portafilters alike, making it a must-have for any serious coffee aficionado.", "قم بقياس عوائد الإسبريسو الخاصة بك بدقة باستخدام كوب إم إتش دبليو أوز العملي والمتين للغاية. يعتبر كوب القياس الزجاجي بسعة ٥٠ مل أداة لا غنى عنها لضبط مطحنة الإسبريسو وإتقان وصفات الاستخلاص اليومية. تضمن لك علامات القياس الواضحة وسهلة القراءة معرفة كمية السائل الموزع بدقة بلمحة واحدة. مصنوع من زجاج فاخر مقاوم للحرارة، ويتحمل درجات الحرارة العالية للإسبريسو الطازج بسهولة دون أن يتشقق أو ينكسر. يتناسب حجمه المدمج بشكل مثالي تحت مقابض الترشيح التجارية والمنزلية على حد سواء، مما يجعله عنصراً أساسياً لأي عاشق جاد للقهوة.", "Accessories", new[] { ("N/A", 31m, 8.37m) }),
            ("MHW Stainless Steel Measuring Cup-100ml", "كوب قياس إم إتش دبليو من الفولاذ المقاوم للصدأ سعة ١٠٠ مل", "Bring a touch of modern sophistication to your brewing routine with the MHW Stainless Steel Measuring Cup. Finished in a stunning matte black, this 100ml cup combines rugged durability with an incredibly sleek aesthetic. It is perfectly sized for measuring larger espresso double shots, catching drips, or portioning out precise amounts of water and milk. The high-grade stainless steel construction ensures it will easily survive accidental drops and the rigorous demands of a busy coffee bar. Furthermore, its smooth interior makes pouring and cleaning an absolute breeze, saving you valuable time during your morning rush.", "أضف لمسة من الرقي الحديث إلى روتين تحضير القهوة الخاص بك مع كوب القياس إم إتش دبليو من الفولاذ المقاوم للصدأ. يتميز هذا الكوب بسعة ١٠٠ مل بلمسة نهائية مذهلة باللون الأسود غير اللامع، ويجمع بين المتانة القوية والجماليات الأنيقة للغاية. حجمه مثالي لقياس جرعات الإسبريسو المزدوجة الأكبر حجماً، أو التقاط القطرات، أو تقسيم كميات دقيقة من الماء والحليب. يضمن الهيكل المصنوع من الفولاذ المقاوم للصدأ عالي الجودة تحمله للسقوط العرضي والمتطلبات الصارمة لمقهى مزدحم بسهولة. علاوة على ذلك، فإن تصميمه الداخلي الأملس يجعل الصب والتنظيف أمراً سهلاً للغاية، مما يوفر لك وقتاً ثميناً خلال اندفاع الصباح.", "Brewing", new[] { ("matte black", 47m, 12.69m) }),
            ("MHW Flash Tamper 2.0-58mm-Flat 2.0", "مكبس قهوة إم إتش دبليو فلاش ٢.٠ مقاس ٥٨ مم بقاعدة مسطحة ٢.٠", "Achieve perfectly level and consistent tamps every single time with the innovative MHW Flash Tamper 2.0. Designed to perfectly fit standard 58mm portafilters, this tamper features a precision-machined Flat 2.0 base for optimal coffee bed compaction. The built-in leveling mechanism completely eliminates the risk of an slanted tamp, dramatically reducing the chance of channeling during extraction. Its highly ergonomic handle provides a comfortable and secure grip, minimizing wrist strain even during high-volume cafe service. Investing in this premium tamping tool will instantly improve the flavor clarity and overall consistency of your espresso shots.", "حقق كبساً مستوياً ومتسقاً تماماً في كل مرة باستخدام مكبس القهوة المبتكر إم إتش دبليو فلاش ٢.٠. تم تصميم هذا المكبس ليتناسب تماماً مع مقابض الترشيح القياسية مقاس ٥٨ مم، ويتميز بقاعدة مسطحة ٢.٠ مصنعة بدقة لضغط طبقة القهوة بشكل مثالي. تقضي آلية التسوية المدمجة تماماً على خطر الكبس المائل، مما يقلل بشكل كبير من فرصة حدوث التوجيه أثناء الاستخلاص. يوفر مقبضه المريح للغاية قبضة مريحة وآمنة، مما يقلل من إجهاد المعصم حتى أثناء تقديم الخدمة بكثافة في المقهى. سيؤدي الاستثمار في أداة الكبس الممتازة هذه إلى تحسين وضوح النكهة والاتساق العام لجرعات الإسبريسو الخاصة بك على الفور.", "Brewing", new[] { ("N/A", 191m, 51.57m) }),
            ("MHW Vase Tamper-58mm universal-Flat", "مكبس قهوة إم إتش دبليو فيز مقاس ٥٨ مم عالمي بقاعدة مسطحة", "Add a touch of natural elegance to your espresso preparation with the beautifully crafted MHW Vase Tamper. Featuring a smooth, ergonomic handle turned from rich walnut wood, it feels incredibly comfortable and perfectly balanced in the hand. The 58mm universal flat base is constructed from heavy-duty stainless steel, providing the perfect weight for an effortless and even tamp. This meticulous construction ensures a perfectly level coffee bed, leading to balanced, sweet, and delicious espresso extractions. It is the perfect blend of traditional craftsmanship and modern barista functionality, making it a proud addition to any coffee bar.", "أضف لمسة من الأناقة الطبيعية إلى تحضير الإسبريسو الخاص بك مع مكبس القهوة إم إتش دبليو فيز المصنوع بشكل جميل. يتميز بمقبض ناعم ومريح مصنوع من خشب الجوز الغني، مما يجعله مريحاً للغاية ومتوازناً تماماً في اليد. القاعدة المسطحة العالمية مقاس ٥٨ مم مصنوعة من الفولاذ المقاوم للصدأ شديد التحمل، مما يوفر الوزن المثالي لكبس سهل ومتساوٍ. يضمن هذا البناء الدقيق الحصول على طبقة قهوة مستوية تماماً، مما يؤدي إلى استخلاص إسبريسو متوازن وحلو ولذيذ. إنه مزيج مثالي من الحرفية التقليدية ووظائف الباريستا الحديثة، مما يجعله إضافة تفخر بها في أي مقهى.", "Brewing", new[] { ("walnut", 72m, 19.44m) }),
            ("MHW Cyclone Gravity Coffee Distributor-58mm universal", "موزع قهوة إم إتش دبليو سايكلون جرافيتي مقاس ٥٨ مم عالمي", "Revolutionize your puck preparation with the cutting-edge MHW Cyclone Gravity Coffee Distributor. This 58mm universal tool utilizes advanced gravity-adapting technology to distribute coffee grounds evenly without the need for tedious manual depth adjustments. The unique cyclone blade design efficiently sweeps the grounds to the edges of the basket, breaking up stubborn clumps and filling any air voids. By ensuring a perfectly homogeneous coffee bed, it dramatically improves extraction yields and overall shot consistency. Crafted from premium materials, this distributor is a heavy-duty, professional-grade accessory that takes all the guesswork out of your espresso routine.", "أحدث ثورة في تحضير قرص القهوة الخاص بك مع موزع القهوة المتطور إم إتش دبليو سايكلون جرافيتي. تستخدم هذه الأداة العالمية مقاس ٥٨ مم تقنية متقدمة للتكيف مع الجاذبية لتوزيع حبيبات القهوة بالتساوي دون الحاجة إلى تعديلات يدوية مملة للعمق. يمسح تصميم الشفرة الإعصارية الفريد الحبيبات بكفاءة إلى حواف السلة، ويكسر التكتلات العنيدة ويملأ أي فراغات هوائية. من خلال ضمان طبقة قهوة متجانسة تماماً، فإنه يحسن عوائد الاستخلاص والاتساق العام للجرعة بشكل كبير. هذا الموزع المصنوع من مواد فاخرة هو ملحق احترافي شديد التحمل يزيل كل التخمين من روتين الإسبريسو الخاص بك.", "Brewing", new[] { ("N/A", 269m, 72.63m) }),
            ("MHW Astra Collection-Portafilter Holder-51-58mm", "حامل مقبض الترشيح مجموعة إم إتش دبليو أسترا مقاس ٥١-٥٨ مم", "Keep your coffee station perfectly organized and your portafilter completely secure with the MHW Astra Collection Portafilter Holder. This versatile stand is brilliantly designed to accommodate a wide range of portafilter sizes, easily adjusting to fit anywhere from 51mm up to 58mm. Finished in a sleek, professional black, it seamlessly blends into the aesthetic of any modern cafe or home barista setup. The holder provides a highly stable platform for tamping, protecting your countertops from damage and ensuring your delicate portafilter spouts remain clean. Its sturdy construction and non-slip base guarantee that it stays firmly in place during even the most vigorous tamping sessions.", "حافظ على محطة القهوة الخاصة بك منظمة تماماً ومقبض الترشيح آمناً تماماً مع حامل مقبض الترشيح مجموعة إم إتش دبليو أسترا. تم تصميم هذا الحامل المتعدد الاستخدامات ببراعة لاستيعاب مجموعة واسعة من أحجام مقابض الترشيح، مع سهولة التعديل ليناسب المقاسات من ٥١ مم إلى ٥٨ مم. بفضل تشطيبه الأسود الأنيق والاحترافي، يمتزج بسلاسة مع جماليات أي مقهى حديث أو إعداد باريستا منزلي. يوفر الحامل منصة مستقرة للغاية للكبس، مما يحمي أسطح العمل من التلف ويضمن بقاء فوهات مقبض الترشيح الرقيقة نظيفة. يضمن هيكله القوي وقاعدته غير القابلة للانزلاق بقاءه ثابتاً في مكانه حتى خلال جلسات الكبس الأكثر قوة.", "Brewing", new[] { ("Black", 226m, 61.02m) }),
            ("MHW Cube Coffee Scale 2.0 Mini", "ميزان قهوة إم إتش دبليو كيوب ٢.٠ ميني", "Maximize your brewing precision with the highly compact and responsive MHW Cube Coffee Scale 2.0 Mini. Despite its incredibly small footprint, this scale is packed with the powerful features essential for perfectly dialing in your espresso shots. It boasts a highly accurate load cell that registers weight changes instantly, ensuring you stop your extraction at the exact right milligram. The sleek black design features a bright, easy-to-read LED display that remains clearly visible even in brightly lit environments. Its miniature size allows it to fit comfortably on the drip tray of virtually any espresso machine, making it a vital tool for consistent daily brewing.", "ضاعف دقة التحضير لديك إلى أقصى حد مع ميزان القهوة إم إتش دبليو كيوب ٢.٠ ميني المدمج للغاية وسريع الاستجابة. على الرغم من حجمه الصغير جداً، إلا أن هذا الميزان مليء بالميزات القوية الأساسية لضبط جرعات الإسبريسو الخاصة بك بشكل مثالي. يتميز بخلية وزن دقيقة للغاية تسجل التغيرات في الوزن على الفور، مما يضمن إيقاف الاستخلاص عند المليغرام الصحيح تماماً. يتميز التصميم الأسود الأنيق بشاشة إل إي دي ساطعة وسهلة القراءة تظل مرئية بوضوح حتى في البيئات الساطعة الإضاءة. يسمح حجمه المصغر بوضعه بشكل مريح على صينية التقطير لأي آلة إسبريسو تقريباً، مما يجعله أداة حيوية للتحضير اليومي المتسق.", "Brewing", new[] { ("Black", 140m, 37.80m) }),
            ("MHW Cube Coffee Scale 3.0 Pro Max", "ميزان قهوة إم إتش دبليو كيوب ٣.٠ برو ماكس", "Take your brewing to the highest professional level with the MHW Cube Coffee Scale 3.0 Pro Max. This premium scale offers lightning-fast response times and unmatched accuracy, making it the ultimate tool for espresso and pour-over enthusiasts alike. It features a larger, more resilient weighing surface and an advanced LED display that clearly presents weight, flow rate, and time simultaneously. Built with a durable, water-resistant casing, it easily handles the messy reality of a busy coffee bar. With smart connectivity and an incredibly long-lasting battery, this flagship scale ensures your extraction parameters are always under perfect control.", "ارتقِ بتحضير القهوة إلى أعلى مستوى احترافي مع ميزان القهوة إم إتش دبليو كيوب ٣.٠ برو ماكس. يوفر هذا الميزان المتميز أوقات استجابة فائقة السرعة ودقة لا مثيل لها، مما يجعله الأداة المثالية لعشاق الإسبريسو والقهوة المقطرة على حد سواء. يتميز بسطح وزن أكبر وأكثر مرونة وشاشة إل إي دي متقدمة تعرض الوزن ومعدل التدفق والوقت بوضوح في وقت واحد. تم تصميمه بغلاف متين ومقاوم للماء، مما يجعله يتعامل بسهولة مع الواقع الفوضوي لمقهى مزدحم. بفضل الاتصال الذكي والبطارية التي تدوم طويلاً بشكل لا يصدق، يضمن هذا الميزان الرائد أن معلمات الاستخلاص الخاصة بك تحت السيطرة المثالية دائماً.", "Brewing", new[] { ("Black", 215m, 58.05m) }),
            ("MHW Formula Smart Coffee Scale", "ميزان قهوة إم إتش دبليو فورميولا الذكي", "Streamline your pour-over and espresso workflow with the highly intuitive MHW Formula Smart Coffee Scale. Available in elegant black or pristine white, this scale seamlessly integrates into any modern coffee setup. It boasts automated tracking features, including auto-tare and auto-start timers, allowing you to focus entirely on your pouring technique. The highly sensitive load cell captures even the slightest changes in water weight, guaranteeing consistency in every cup. With its sleek, minimalist design and easy-to-clean surface, it is a smart investment for anyone serious about specialty coffee.", "قم بتبسيط سير عمل القهوة المقطرة والإسبريسو باستخدام ميزان القهوة إم إتش دبليو فورميولا الذكي والبديهي للغاية. يتوفر هذا الميزان باللون الأسود الأنيق أو الأبيض النقي، ويندمج بسلاسة في أي إعداد قهوة حديث. يتميز بميزات تتبع آلية، بما في ذلك التصفير التلقائي ومؤقتات التشغيل التلقائي، مما يتيح لك التركيز كلياً على تقنية الصب الخاصة بك. تلتقط خلية الوزن شديدة الحساسية حتى أدنى التغيرات في وزن الماء، مما يضمن الاتساق في كل كوب. بفضل تصميمه الأنيق والبسيط وسطحه سهل التنظيف، يعد استثماراً ذكياً لأي شخص جاد بشأن القهوة المختصة.", "Brewing", new[] { ("Black", 195m, 52.65m), ("White", 195m, 52.65m) }),
            ("MHW Coffee Knock Box-6.8L [Including 1 Garbage Bag and 1 Towel]", "صندوق تفريغ القهوة إم إتش دبليو سعة ٦.٨ لتر [يشمل ١ كيس قمامة و ١ منشفة]", "Efficiently manage high-volume espresso waste with the heavy-duty MHW Coffee Knock Box. Featuring a massive 6.8L capacity, this commercial-grade knock box minimizes the frequency of emptying during peak cafe hours. The thick, shock-absorbing knock bar is designed to withstand the hardest strikes while protecting your expensive portafilters and dampening noise. It comes complete with a custom-fit garbage bag and a handy barista towel for immediate setup and convenience. The sturdy, non-slip base ensures the unit remains firmly anchored to your counter, keeping your workstation clean and efficient.", "قم بإدارة نفايات الإسبريسو ذات الحجم الكبير بكفاءة مع صندوق تفريغ القهوة إم إتش دبليو شديد التحمل. يتميز هذا الصندوق ذو الجودة التجارية بسعة هائلة تبلغ ٦.٨ لتر، مما يقلل من تكرار التفريغ خلال ساعات الذروة في المقهى. تم تصميم قضيب التفريغ السميك الممتص للصدمات لتحمل أقوى الضربات مع حماية مقابض الترشيح باهظة الثمن وتقليل الضوضاء. يأتي مزوداً بكيس قمامة مناسب ومنشفة باريستا يدوية للإعداد الفوري والراحة. تضمن القاعدة القوية غير القابلة للانزلاق بقاء الوحدة ثابتة بإحكام على سطح العمل، مما يحافظ على نظافة وكفاءة محطة العمل الخاصة بك.", "Cleaning & Maintenance", new[] { ("black", 503m, 135.81m) }),
            ("MHW Coffee Knock Box Garbage Bag-30pcs", "أكياس قمامة لصندوق تفريغ القهوة إم إتش دبليو - ٣٠ قطعة", "Maintain impeccable hygiene around your espresso machine with this convenient 30-piece pack of MHW Coffee Knock Box Garbage Bags. Specifically tailored to perfectly fit your large-capacity knock box, these bags prevent wet coffee grounds from sticking to the interior walls, making cleanup a breeze. Constructed from durable, tear-resistant materials, they confidently hold heavy, damp espresso pucks without leaking or breaking. By streamlining your waste disposal process, these essential liners save baristas valuable time and effort at the end of a long shift. Keep your coffee bar sanitary and organized with this simple yet highly effective solution.", "حافظ على نظافة لا تشوبها شائبة حول آلة الإسبريسو الخاصة بك مع هذه العبوة المريحة المكونة من ٣٠ قطعة من أكياس قمامة صندوق تفريغ القهوة إم إتش دبليو. مصممة خصيصاً لتناسب صندوق التفريغ ذو السعة الكبيرة بشكل مثالي، تمنع هذه الأكياس بقايا القهوة الرطبة من الالتصاق بالجدران الداخلية، مما يجعل التنظيف أمراً سهلاً. مصنوعة من مواد متينة ومقاومة للتمزق، فهي تحمل أقراص الإسبريسو الثقيلة والرطبة بثقة دون تسريب أو تمزق. من خلال تبسيط عملية التخلص من النفايات، توفر هذه الأكياس الأساسية للباريستا وقتاً وجهداً ثمينين في نهاية نوبة عمل طويلة. حافظ على مقهاك صحياً ومنظماً مع هذا الحل البسيط والفعال للغاية.", "Cleaning & Maintenance", new[] { ("N/A", 74m, 19.98m) }),
            ("MHW Magnetic Dosing Ring-58mm universal", "حلقة جرعات مغناطيسية إم إتش دبليو مقاس ٥٨ مم عالمي", "Eliminate messy coffee grounds and reduce waste with the cleverly designed MHW Magnetic Dosing Ring. Engineered to universally fit 58mm portafilters, this dosing funnel features strong integrated magnets that snap securely into place, ensuring it will not budge during grinding or distribution. By keeping all the grounds safely inside the basket, it promotes a cleaner workstation and ensures your dosing remains completely accurate. Made from lightweight yet durable aluminum, it is exceptionally easy to attach, remove, and clean. This is an absolute must-have accessory for anyone using WDT tools or seeking a flawless puck preparation routine.", "تخلص من فوضى حبيبات القهوة وقلل من الهدر باستخدام حلقة الجرعات المغناطيسية إم إتش دبليو المصممة ببراعة. تم تصميم قمع الجرعات هذا ليناسب مقابض الترشيح مقاس ٥٨ مم بشكل عالمي، ويتميز بمغناطيسات مدمجة قوية تثبت بإحكام في مكانها، مما يضمن عدم تحركها أثناء الطحن أو التوزيع. من خلال الحفاظ على جميع الحبيبات بأمان داخل السلة، فإنه يعزز محطة عمل أكثر نظافة ويضمن بقاء جرعاتك دقيقة تماماً. مصنوع من الألومنيوم خفيف الوزن ولكنه متين، ومن السهل للغاية تركيبه وإزالته وتنظيفه. هذا ملحق لا بد منه لأي شخص يستخدم أدوات توزيع القهوة أو يبحث عن روتين مثالي لتحضير قرص القهوة.", "Brewing", new[] { ("N/A", 59m, 15.93m) }),
            ("MHW Assassin electric pour over kettle [UK]", "غلاية قهوة مقطرة كهربائية إم إتش دبليو أساسين [قابس بريطاني]", "Gain absolute mastery over your manual brewing with the high-performance MHW Assassin Electric Pour Over Kettle. Available in striking black or white, this kettle features an expertly crafted gooseneck spout that provides unparalleled precision and control over your water flow rate. The advanced digital base allows for exact temperature selection, ensuring your water is heated perfectly for any specific coffee roast. It rapidly brings water to a boil and steadily maintains the desired temperature for consistent, repeatable extractions. Combining striking aesthetics with top-tier functionality, it is the ultimate centerpiece for any pour-over coffee bar.", "احصل على سيطرة مطلقة على التحضير اليدوي للقهوة باستخدام غلاية القهوة المقطرة الكهربائية عالية الأداء إم إتش دبليو أساسين. تتوفر هذه الغلاية باللون الأسود أو الأبيض المذهل، وتتميز بفوهة عنق الإوزة المصنوعة بخبرة والتي توفر دقة وتحكماً لا مثيل لهما في معدل تدفق المياه. تتيح القاعدة الرقمية المتقدمة اختياراً دقيقاً لدرجة الحرارة، مما يضمن تسخين المياه بشكل مثالي لأي درجة تحميص معينة للقهوة. تقوم بغلي الماء بسرعة وتحافظ بثبات على درجة الحرارة المطلوبة لاستخلاصات متسقة وقابلة للتكرار. تجمع بين الجماليات المذهلة والوظائف عالية المستوى، وهي القطعة المركزية المطلقة لأي ركن للقهوة المقطرة.", "Brewing", new[] { ("White", 429m, 115.83m), ("Black", 429m, 115.83m) }),
            ("MHW Smooth Espresso Chilling Ball Stand-1pcs", "حامل كرة تبريد الإسبريسو إم إتش دبليو سموث - ١ قطعة", "Preserve the delicate volatile aromatics of your espresso with the innovative MHW Smooth Espresso Chilling Ball Stand. This specialized tool allows you to suspend a frozen stainless steel chilling ball directly over your cup, rapidly cooling the espresso as it extracts to lock in maximum sweetness and complex flavors. Available in sophisticated black or sleek silver finishes, the stand is adjustable to fit various cup sizes and espresso machines. It is built from high-quality materials to resist corrosion and ensure long-lasting durability in a damp environment. Unlock a completely new dimension of flavor clarity in your favorite coffee beans with this cutting-edge extraction method.", "حافظ على المركبات العطرية المتطايرة الرقيقة للإسبريسو باستخدام حامل كرة تبريد الإسبريسو إم إتش دبليو سموث المبتكر. تتيح لك هذه الأداة المتخصصة تعليق كرة تبريد مجمدة من الفولاذ المقاوم للصدأ مباشرة فوق الكوب الخاص بك، مما يبرد الإسبريسو بسرعة أثناء الاستخلاص لحبس أقصى قدر من الحلاوة والنكهات المعقدة. يتوفر الحامل بتشطيبات سوداء راقية أو فضية أنيقة، وهو قابل للتعديل ليناسب أحجام الأكواب المختلفة وآلات الإسبريسو. تم تصميمه من مواد عالية الجودة لمقاومة التآكل وضمان المتانة طويلة الأمد في بيئة رطبة. اكتشف بعداً جديداً تماماً لوضوح النكهة في حبوب القهوة المفضلة لديك باستخدام طريقة الاستخلاص المتطورة هذه.", "Brewing", new[] { ("Black", 70m, 18.90m), ("Silver", 70m, 18.90m) }),
            ("MHW Elf Coffee Server-500ml", "وعاء تقديم القهوة إم إتش دبليو إلف سعة ٥٠٠ مل", "Elegantly serve your freshly brewed pour-over or drip coffee with the beautiful MHW Elf Coffee Server. Crafted from premium high-borosilicate glass, this 500ml server offers excellent heat retention while remaining remarkably clear, allowing you to appreciate the rich color of your brew. Its gracefully curved design is not only visually pleasing but also engineered to swirl and aerate the coffee, enhancing its aromatic profile before serving. The V-shaped spout guarantees a smooth, drip-free pour every single time. Compatible with most standard coffee drippers, it is a versatile and essential piece of glassware for any coffee enthusiast.", "قدم قهوتك المقطرة الطازجة بأناقة مع وعاء تقديم القهوة إم إتش دبليو إلف الجميل. تم صنع هذا الوعاء سعة ٥٠٠ مل من زجاج البورسليكات العالي الجودة، ويوفر احتفاظاً ممتازاً بالحرارة مع بقائه شفافاً بشكل ملحوظ، مما يتيح لك الاستمتاع باللون الغني لقهوتك. تصميمه المنحني برشاقة ليس فقط ممتعاً بصرياً ولكنه مصمم أيضاً لتدوير القهوة وتهويتها، مما يعزز مظهرها العطري قبل التقديم. تضمن الفوهة على شكل حرف في صباً سلساً وخالياً من التقطير في كل مرة. متوافق مع معظم أقماع تقطير القهوة القياسية، وهو قطعة زجاجية متعددة الاستخدامات وأساسية لأي عاشق للقهوة.", "Accessories", new[] { ("Transparent", 59m, 15.93m) }),
            ("MHW Snail Filter Paper Holder", "حامل فلاتر ورقية إم إتش دبليو سنيل", "Keep your coffee brewing filters pristine, organized, and easily accessible with the uniquely designed MHW Snail Filter Paper Holder. Its charming, snail-inspired shape adds a touch of modern artistic flair to your coffee bar while serving a highly practical purpose. Finished in a sleek matte black, it accommodates a large capacity of cone-shaped or flat-bottom filters, protecting them from dust, moisture, and unwanted wrinkles. The wide, stable base prevents tipping, ensuring your delicate paper filters are always ready for your next brew. It is the perfect blend of playful design and essential barista organization.", "حافظ على فلاتر تحضير القهوة الخاصة بك نقية ومنظمة ويمكن الوصول إليها بسهولة باستخدام حامل الفلاتر الورقية إم إتش دبليو سنيل المصمم بشكل فريد. يضيف شكله الساحر المستوحى من الحلزون لمسة من الذوق الفني الحديث إلى ركن القهوة الخاص بك بينما يخدم غرضاً عملياً للغاية. بفضل اللمسة النهائية السوداء غير اللامعة الأنيقة، فإنه يستوعب سعة كبيرة من الفلاتر المخروطية الشكل أو ذات القاعدة المسطحة، مما يحميها من الغبار والرطوبة والتجاعيد غير المرغوب فيها. تمنع القاعدة العريضة والمستقرة الانقلاب، مما يضمن أن الفلاتر الورقية الرقيقة جاهزة دائماً لتحضيرك القادم. إنه مزيج مثالي من التصميم المرح والتنظيم الأساسي للباريستا.", "Accessories", new[] { ("black", 70m, 18.90m) }),
            ("MHW Paper Filter-100pcs/box-V02", "فلاتر ورقية إم إتش دبليو - ١٠٠ قطعة/صندوق - مقاس في ٠٢", "Achieve incredibly clean and bright tasting pour-over coffee with the MHW Paper Filters, designed specifically for V02 style drippers. This generous box contains 100 high-quality filters crafted from premium natural wood pulp, completely free of any bleaches or glues that could impart unwanted papery tastes to your brew. The specialized paper density ensures an optimal flow rate, effectively trapping fine coffee sediments and bitter oils while allowing the distinct, vibrant flavor notes of your beans to shine through. The sturdy, folded seam construction prevents tearing during the blooming and brewing process, guaranteeing a perfectly filtered cup every morning.", "احصل على قهوة مقطرة ذات مذاق نقي ومشرق بشكل لا يصدق مع فلاتر إم إتش دبليو الورقية، المصممة خصيصاً لأقماع التقطير مقاس في ٠٢. يحتوي هذا الصندوق السخي على ١٠٠ فلتر عالي الجودة مصنوع من لب الخشب الطبيعي الفاخر، وخالي تماماً من أي مبيضات أو مواد لاصقة يمكن أن تضفي أذواقاً ورقية غير مرغوب فيها على قهوتك. تضمن كثافة الورق المتخصصة معدل تدفق مثالي، مما يحبس رواسب القهوة الدقيقة والزيوت المرة بفعالية مع السماح لنكهات حبوب القهوة النابضة بالحياة والمميزة بالتألق. يمنع البناء القوي للدرزة المطوية التمزق أثناء عملية الترطيب والتحضير، مما يضمن الحصول على كوب مصفى بشكل مثالي كل صباح.", "Brewing", new[] { ("N/A", 51m, 13.77m) }),
            ("MHW Paper Filter V01 100pcs", "فلاتر ورقية إم إتش دبليو ١٠٠ حبة مقاس في ٠١", "Experience clean and balanced extractions with the MHW V01 Paper Filters. Made from natural, eco-friendly wood pulp without any bleach or fluorescent agents, these cone-shaped filters ensure the pure taste of your pour-over coffee. Perfect for V60-style drippers, this 100-piece box is ideal for daily single-cup brewing, offering minimal residue and effortless cleanup.", "استمتع باستخلاص نقي ومتوازن مع فلاتر إم إتش دبليو الورقية مقاس V01. هذه الفلاتر مخروطية الشكل مصنوعة من لب الخشب الطبيعي الصديق للبيئة بدون أي مبيضات أو مواد فلورية، مما يضمن المذاق النقي لقهوتك المقطرة. يتناسب هذا الصندوق الذي يحتوي على 100 حبة بشكل مثالي مع قمع الترشيح V60، وهو مثالي للتحضير اليومي لكوب واحد، مما يوفر أقل قدر من الرواسب وسهولة في التنظيف.", "Brewing", new[] { ("N/A", 39m, 10.53m) }),
            ("MHW Silicone Dish Drying Pad 400x300mm", "حصيرة تجفيف الأطباق من السيليكون إم إتش دبليو ٤٠٠×٣٠٠ مم", "Keep your coffee bar clean and organized with the MHW Silicone Dish Drying Pad. Measuring 400x300mm, this high-quality, food-grade silicone mat features a ridged design that promotes airflow and rapid drying for your coffee cups, portafilters, and accessories. Its durable, non-slip surface protects your countertops from scratches and spills, making it an essential addition to any barista station.", "حافظ على نظافة وتنظيم ركن القهوة الخاص بك مع حصيرة تجفيف الأطباق المصنوعة من السيليكون من إم إتش دبليو. بمقاس 400×300 مم، تتميز هذه الحصيرة المصنوعة من السيليكون عالي الجودة والآمن غذائياً بتصميم مضلع يعزز تدفق الهواء والتجفيف السريع لأكواب القهوة وبورتافلتر القهوة والملحقات. يحمي سطحها المتين غير القابل للانزلاق أسطح العمل من الخدوش والانسكابات، مما يجعلها إضافة أساسية لأي محطة باريستا.", "Cleaning & Maintenance", new[] { ("black", 125m, 33.75m) }),
            ("MHW YU Series Tamping Base", "قاعدة كبس إم إتش دبليو سلسلة يو", "Bring clarity and structure to your espresso station with the MHW YU Series Tamping Base. Crafted from durable, eco-friendly silicone, this flexible mat is designed to perfectly organize your tamping tools, featuring dedicated slots for tampers, dosing rings, needle distributors, and your portafilter. It protects your countertop and equipment while ensuring a smooth, consistent workflow for your daily espresso preparation.", "أضف الترتيب والتنظيم إلى ركن الإسبريسو الخاص بك مع قاعدة الكبس من سلسلة يو من إم إتش دبليو. هذه الحصيرة المرنة مصنوعة من السيليكون المتين والصديق للبيئة، وهي مصممة لتنظيم أدوات الكبس الخاصة بك بشكل مثالي، حيث تحتوي على فتحات مخصصة للمكابس وحلقات الجرعات وموزعات الإبر والبورتافلتر. تحمي أسطح العمل والمعدات الخاصة بك مع ضمان سير عمل سلس ومتسق لتحضير الإسبريسو اليومي.", "Brewing", new[] { ("Black", 109m, 29.43m) }),
            ("MHW Universal Filter Basket 58mm 18g", "سلة فلتر إم إتش دبليو ٥٨ مم شاملة ١٨ غرام", "Enhance your espresso extraction with the MHW 58mm Universal Filter Basket. Designed to hold an 18g dose, this precision-crafted stainless steel basket fits most standard 58mm portafilters. It features precisely engineered micro-holes for optimal water flow, reducing channeling and ensuring a rich, balanced, and flavorful espresso shot every time you brew.", "عزز استخلاص الإسبريسو الخاص بك مع سلة الفلتر الشاملة مقاس 58 مم من إم إتش دبليو. صُممت هذه السلة المصنوعة بدقة من الستانلس ستيل لتستوعب جرعة 18 غراماً، وتتناسب مع معظم مقابض البورتافلتر القياسية مقاس 58 مم. تتميز بثقوب دقيقة مصممة هندسياً لتدفق المياه بشكل مثالي، مما يقلل من التسرب الجانبي (التشانلينج) ويضمن الحصول على كوب إسبريسو غني ومتوازن ولذيذ في كل مرة تحضر فيها القهوة.", "Brewing", new[] { ("N/A", 47m, 12.69m) }),
            ("MHW BEP Butterfly Basket 2.0 58mm 20g", "سلة فلتر الفراشة بيب ٢.٠ من إم إتش دبليو ٥٨ مم ٢٠ غرام", "Experience next-level extraction with the MHW BEP Butterfly Basket 2.0. This universally compatible 58mm basket accommodates a 20g dose and is crafted from premium SUS304 stainless steel. Its unique \"butterfly\" hole pattern is engineered to maximize extraction yield and clarity, making it a perfect upgrade for professional baristas and home enthusiasts seeking perfect espresso.", "اختبر مستوى جديداً من الاستخلاص مع سلة فلتر الفراشة بيب 2.0 من إم إتش دبليو. تتوافق هذه السلة الشاملة مقاس 58 مم مع جرعة 20 غراماً ومصنوعة من الستانلس ستيل الفاخر. صُمم نمط الثقوب الفريد على شكل \"فراشة\" لزيادة إنتاجية الاستخلاص والوضوح إلى أقصى حد، مما يجعلها ترقية مثالية للباريستا المحترفين والمتحمسين المنزليين الذين يبحثون عن إسبريسو مثالي.", "Brewing", new[] { ("N/A", 62m, 16.74m) }),
            ("MHW BEP Butterfly Basket 2.0 51mm 12g", "سلة فلتر الفراشة بيب ٢.٠ من إم إتش دبليو ٥١ مم ١٢ غرام", "Tailored for smaller machines, the MHW BEP Butterfly Basket 2.0 brings professional-grade extraction to 51mm portafilters. Accommodating a 12g dose, this stainless steel basket features the innovative \"butterfly\" micro-hole layout designed to promote even water distribution and prevent channeling, resulting in sweeter, more balanced espresso shots from your home setup.", "صُممت سلة فلتر الفراشة بيب 2.0 من إم إتش دبليو خصيصاً للآلات الأصغر حجماً، لتجلب استخلاصاً احترافياً إلى مقابض البورتافلتر مقاس 51 مم. تستوعب هذه السلة المصنوعة من الستانلس ستيل جرعة 12 غراماً وتتميز بتصميم الثقوب الدقيقة المبتكر على شكل \"فراشة\" المصمم لتعزيز التوزيع المتساوي للمياه ومنع التسرب الجانبي، مما ينتج عنه أكواب إسبريسو أكثر حلاوة وتوازناً من أجهزتك المنزلية.", "Brewing", new[] { ("N/A", 62m, 16.74m) }),
            ("MHW Elbow Brush 220mm", "فرشاة تنظيف بمقبض مائل إم إتش دبليو ٢٢٠ مم", "Keep your espresso machine's group head spotless with the MHW Elbow Brush. Measuring 220mm, this brush features an ergonomic 30-degree angled handle and tough nylon (PA) bristles, making it incredibly easy to reach tight spaces around the gasket and group head. It effectively removes stubborn coffee residues while keeping your hands safely away from hot water.", "حافظ على نظافة رأس المجموعة في آلة الإسبريسو الخاصة بك مع فرشاة التنظيف ذات المقبض المائل من إم إتش دبليو. يبلغ طول هذه الفرشاة 220 مم، وتتميز بمقبض مريح بزاوية 30 درجة وشعيرات نايلون قوية، مما يجعل من السهل جداً الوصول إلى الأماكن الضيقة حول الحشية ورأس المجموعة. تعمل على إزالة بقايا القهوة العنيدة بفعالية مع إبقاء يديك بعيداً بأمان عن الماء الساخن.", "Cleaning & Maintenance", new[] { ("Black", 31m, 8.37m) }),
            ("MHW Universal Blind Bowl 58mm", "سلة فلتر مغلقة (وعاء أعمى) إم إتش دبليو ٥٨ مم", "Maintain the internal hygiene of your espresso machine with the MHW 58mm Universal Blind Bowl. This solid stainless steel insert has no holes and is essential for backflushing your machine's group head. Compatible with most standard 58mm commercial and prosumer portafilters, it helps clear out old coffee oils and grounds when used with cleaning powder, prolonging the life of your machine.", "حافظ على النظافة الداخلية لآلة الإسبريسو الخاصة بك مع سلة الفلتر المغلقة (الوعاء الأعمى) الشاملة مقاس 58 مم من إم إتش دبليو. هذه السلة الصلبة المصنوعة من الستانلس ستيل لا تحتوي على ثقوب وهي ضرورية لعملية الغسيل العكسي لرأس المجموعة في آلتك. تتوافق مع معظم مقابض البورتافلتر القياسية مقاس 58 مم، وتساعد على التخلص من زيوت القهوة القديمة والرواسب عند استخدامها مع مسحوق التنظيف، مما يطيل من عمر آلتك.", "Cleaning & Maintenance", new[] { ("silver spot", 31m, 8.37m) }),
            ("MHW Waffle Towel 30x30cm", "منشفة وافل إم إتش دبليو ٣٠×٣٠ سم", "Wipe down your steam wands and coffee bar with ease using the MHW Waffle Towel. Sized at a convenient 30x30cm, this dark grey towel features a highly absorbent, textured waffle weave that effectively traps moisture, coffee grinds, and milk residue. Its durable, lint-free material makes it the perfect accessory for maintaining a pristine workstation and extending the cleanliness of your equipment.", "نظف عصا التبخير وركن القهوة الخاص بك بسهولة باستخدام منشفة الوافل من إم إتش دبليو. بحجم مريح يبلغ 30×30 سم، تتميز هذه المنشفة ذات اللون الرمادي الداكن بنسيج وافل عالي الامتصاص يحبس الرطوبة وحبيبات القهوة وبقايا الحليب بفعالية. مادتها المتينة والخالية من الوبر تجعلها الملحق المثالي للحفاظ على مساحة عمل نقية وإطالة نظافة معداتك.", "Cleaning & Maintenance", new[] { ("dark grey", 27m, 7.29m) }),
            ("MHW Hanging Ring Towel 30x50cm", "منشفة بحلقة تعليق إم إتش دبليو ٣٠×٥٠ سم", "Keep your hands and coffee equipment dry with the highly practical MHW Hanging Ring Towel. Measuring 30x50cm, this black towel is woven from premium, ultra-absorbent material. It comes equipped with a convenient metal hanging ring, allowing you to easily clip it to your barista apron, belt, or espresso machine handle for instant access during busy brewing sessions.", "حافظ على جفاف يديك ومعدات القهوة مع المنشفة العملية للغاية المزودة بحلقة تعليق من إم إتش دبليو. يبلغ مقاس هذه المنشفة السوداء 30×50 سم، وهي منسوجة من مواد فاخرة وفائقة الامتصاص. تأتي مزودة بحلقة تعليق معدنية مريحة، مما يسمح لك بتثبيتها بسهولة في مريلة الباريستا أو الحزام أو مقبض آلة الإسبريسو للوصول الفوري إليها أثناء جلسات تحضير القهوة المزدحمة.", "Cleaning & Maintenance", new[] { ("black", 31m, 8.37m) }),
            ("MHW Cup Rinser 32.5x17.5cm (S)", "غسالة أكواب إم إتش دبليو ٣٢.٥×١٧.٥ سم (صغير)", "Streamline your bar workflow and maintain impeccable hygiene with the MHW Cup Rinser. Sized at a compact 32.5x17.5cm, this stainless steel rinser effortlessly installs into your countertop, providing a dedicated station to rapidly blast away milk and coffee residue from pitchers and glasses. A simple press activates high-pressure water jets, saving valuable time during busy shifts and ensuring your drinkware is instantly ready for the next customer.", "قم بتبسيط سير عمل البار الخاص بك وحافظ على نظافة لا تشوبها شائبة مع غسالة أكواب إم إتش دبليو. بحجم مدمج يبلغ ٣٢.٥×١٧.٥ سم، يتم تركيب هذه الغسالة المصنوعة من الستانلس ستيل بسهولة في سطح العمل الخاص بك، مما يوفر محطة مخصصة للتخلص السريع من بقايا الحليب والقهوة من أباريق التبخير والأكواب. بضغطة بسيطة يتم تنشيط نفاثات المياه عالية الضغط، مما يوفر وقتاً ثميناً خلال نوبات العمل المزدحمة ويضمن أن تكون أدوات الشرب الخاصة بك جاهزة على الفور للعميل التالي.", "Cleaning & Maintenance", new[] { ("silver", 293m, 79.11m) }),
            ("MHW Embedded Stainless Steel Cup Rinser 19.5x59.5cm", "غسالة أكواب مدمجة من الستانلس ستيل إم إتش دبليو ١٩.٥×٥٩.٥ سم", "Designed for high-volume commercial cafes, the MHW Embedded Stainless Steel Cup Rinser offers an expansive 19.5x59.5cm draining and rinsing area. Its flush-mount design seamlessly integrates into your bar top for a sleek, professional appearance. The powerful multi-angle spray jets ensure deep and thorough cleaning of milk pitchers, tall glasses, and espresso cups in seconds. Its extended drip tray design provides ample space for wet vessels to air dry safely.", "تم تصميم غسالة الأكواب المدمجة المصنوعة من الستانلس ستيل إم إتش دبليو للمقاهي التجارية المزدحمة، وتوفر مساحة واسعة للتصريف والشطف تبلغ ١٩.٥×٥٩.٥ سم. يندمج تصميمها المدمج بسلاسة في سطح البار الخاص بك للحصول على مظهر أنيق واحترافي. تضمن نفاثات الرش القوية متعددة الزوايا تنظيفاً عميقاً وشاملاً لأباريق الحليب والأكواب الطويلة وأكواب الإسبريسو في ثوانٍ. يوفر تصميم صينية التقطير الممتدة مساحة واسعة للأوعية المبللة لتجف في الهواء بأمان.", "Cleaning & Maintenance", new[] { ("silver", 858m, 231.66m) }),
            ("MHW Milk Pitcher 3.0 Sharp Spout 600ml", "إبريق تبخير الحليب إم إتش دبليو ٣.٠ بفوهة حادة سعة ٦٠٠ مل", "Master the art of latte design with the MHW Milk Pitcher 3.0. Featuring a generous 600ml capacity and an expertly engineered sharp spout, this pitcher provides the ultimate precision required for pouring intricate latte art patterns. Its ergonomic handle ensures a comfortable grip and steady control, while the premium stainless steel construction allows for optimal heat transfer and perfectly textured microfoam every time you steam.", "أتقن فن تصميم اللاتيه مع إبريق تبخير الحليب إم إتش دبليو ٣.٠. يتميز هذا الإبريق بسعة كبيرة تبلغ ٦٠٠ مل وفوهة حادة مصممة بخبرة، مما يوفر الدقة القصوى المطلوبة لصب أنماط فن اللاتيه المعقدة. يضمن مقبضه المريح قبضة محكمة وتحكماً ثابتاً، بينما يسمح الهيكل المصنوع من الستانلس ستيل الفاخر بنقل الحرارة بشكل مثالي ورغوة دقيقة ذات قوام مثالي في كل مرة تقوم فيها بالتبخير.", "Brewing", new[] { ("Matte Black", 144m, 38.88m) }),
            ("MHW Wright Cup 90ml", "كوب رايت إم إتش دبليو ٩٠ مل", "Savor your single or double shots in the elegantly crafted MHW Wright Cup. With a perfect 90ml capacity, this tasting cup is meticulously designed to highlight the crema, body, and aroma of your espresso. Its distinctive shape not only provides a comfortable hold but also directs the coffee perfectly to your palate, enhancing the sensory experience of every sip. Ideal for both home espresso bars and professional cafes.", "تذوق جرعة الإسبريسو الفردية أو المزدوجة في كوب رايت إم إتش دبليو المصنوع بأناقة. بسعة مثالية تبلغ ٩٠ مل، تم تصميم كوب التذوق هذا بدقة لإبراز الكريما وقوام ورائحة الإسبريسو. لا يوفر شكله المميز قبضة مريحة فحسب، بل يوجه القهوة بشكل مثالي إلى حاسة التذوق لديك، مما يعزز التجربة الحسية لكل رشفة. مثالي لكل من بارات الإسبريسو المنزلية والمقاهي المحترفة.", "Accessories", new[] { ("transparent", 55m, 14.85m) }),
            ("MHW Cream Double-layer Cup 300ml", "كوب كريم مزدوج الجدار إم إتش دبليو ٣٠٠ مل", "Enjoy your favorite hot or cold beverages in the stylish and practical MHW Cream Double-layer Cup. The 300ml capacity is perfect for lattes, flat whites, or pour-over coffee. Its innovative double-wall insulation keeps your drink at the ideal temperature for longer, while ensuring the outer surface remains comfortable to the touch, entirely eliminating condensation and the need for coasters.", "استمتع بمشروباتك الساخنة أو الباردة المفضلة في كوب كريم مزدوج الجدار الأنيق والعملي من إم إتش دبليو. السعة البالغة ٣٠٠ مل مثالية للاتيه أو الفلات وايت أو القهوة المقطرة. يحافظ العزل المبتكر المزدوج الجدار على مشروبك في درجة الحرارة المثالية لفترة أطول، مع ضمان بقاء السطح الخارجي مريحاً للمس، مما يقضي تماماً على التكثف والحاجة إلى قواعد الأكواب.", "Accessories", new[] { ("N/A", 109m, 29.43m) }),
            ("MHW CERA Series Ceramic Cup 240ml", "كوب سيراميك إم إتش دبليو سلسلة سيرا ٢٤٠ مل", "Bring a touch of timeless sophistication to your morning routine with the MHW CERA Series Ceramic Cup. With a 240ml volume, it is the classic size for traditional cappuccinos or comforting drip coffee. Crafted from dense, high-quality ceramic, it offers outstanding heat retention to keep your coffee warmer for longer. The cup’s smooth glaze and minimalist aesthetic make it a stunning addition to any table setting.", "أضف لمسة من الرقي الخالد إلى روتينك الصباحي مع كوب السيراميك إم إتش دبليو من سلسلة سيرا. بحجم ٢٤٠ مل، فهو الحجم الكلاسيكي للكابتشينو التقليدي أو القهوة المقطرة المريحة. مصنوع من سيراميك كثيف وعالي الجودة، ويوفر احتفاظاً ممتازاً بالحرارة للحفاظ على قهوتك دافئة لفترة أطول. الطلاء الأملس للكوب والجماليات البسيطة تجعله إضافة مذهلة لأي إعداد طاولة.", "Accessories", new[] { ("Black", 140m, 37.80m) }),
            ("MHW A-Shaped Double Wall Glass 130ml", "كوب زجاجي مزدوج الجدار على شكل حرف إيه إم إتش دبليو ١٣٠ مل", "Showcase the beautiful layers of your macchiatos or cortados with the MHW A-Shaped Double Wall Glass. Holding 130ml, its unique outward-flaring \"A\" silhouette creates a mesmerizing floating effect for your beverage. The thermally insulated double-glass design protects your fingers from heat while maintaining the optimal drinking temperature of your coffee, blending visual artistry with everyday practicality.", "اعرض الطبقات الجميلة للمكياتو أو الكورتادو الخاص بك مع الكوب الزجاجي المزدوج الجدار على شكل حرف A من إم إتش دبليو. بسعة ١٣٠ مل، تخلق صورته الظلية الفريدة المتوهجة للخارج تأثيراً عائماً ساحراً لمشروبك. يحمي التصميم الزجاجي المزدوج المعزول حرارياً أصابعك من الحرارة مع الحفاظ على درجة حرارة الشرب المثالية لقهوتك، مما يمزج بين الفن البصري والتطبيق العملي اليومي.", "Accessories", new[] { ("Transparent", 78m, 21.06m), ("Black", 94m, 25.38m) }),
            ("MHW H-Shaped Double Wall Glass 160ml", "كوب زجاجي مزدوج الجدار على شكل حرف إتش إم إتش دبليو ١٦٠ مل", "Elevate your coffee presentation with the modern geometry of the MHW H-Shaped Double Wall Glass. This 160ml glass is ideal for serving flat whites or small lattes. The straight, \"H\" profile design offers a contemporary look and a highly comfortable grip. Crafted from premium borosilicate glass, its double-walled construction ensures excellent temperature stability for both piping hot and ice-cold drinks.", "ارتقِ بتقديم قهوتك مع الهندسة الحديثة للكوب الزجاجي المزدوج الجدار على شكل حرف H من إم إتش دبليو. هذا الكوب سعة ١٦٠ مل مثالي لتقديم الفلات وايت أو اللاتيه الصغير. يوفر التصميم المستقيم والمقطع الجانبي مظهراً عصرياً وقبضة مريحة للغاية. مصنوع من زجاج البورسليكات الفاخر، ويضمن هيكله المزدوج الجدار استقراراً ممتازاً في درجة الحرارة لكل من المشروبات الساخنة والباردة.", "Accessories", new[] { ("Transparent", 78m, 21.06m), ("Black", 94m, 25.38m) }),
            ("MHW YU Series Unibody Bottomless Portafilter Flat 58mm", "بورتافلتر مكشوف قطعة واحدة إم إتش دبليو سلسلة يو مسطح ٥٨ مم", "Perfect your puck prep and visually diagnose your extractions with the premium MHW YU Series Unibody Bottomless Portafilter. Designed for 58mm group heads, this naked portafilter allows you to observe the mesmerizing flow of your espresso, helping to identify channeling and improve your tamping technique. Its innovative unibody flat-bottom design provides unparalleled stability when resting on a tamping mat, streamlining your workflow.", "أتقن تحضير قرص القهوة وقم بتشخيص استخلاصاتك بصرياً باستخدام البورتافلتر المكشوف المكون من قطعة واحدة الفاخر من إم إتش دبليو سلسلة يو. صُمم هذا البورتافلتر المكشوف ليناسب رؤوس المجموعة مقاس ٥٨ مم، ويتيح لك مراقبة التدفق الساحر للإسبريسو الخاص بك، مما يساعد في تحديد التسرب الجانبي وتحسين تقنية الكبس الخاصة بك. يوفر تصميمه المبتكر ذو القاعدة المسطحة المكون من قطعة واحدة ثباتاً لا مثيل له عند وضعه على حصيرة الكبس، مما يبسط سير عملك.", "Brewing", new[] { ("N/A", 772m, 208.44m) }),
            ("MHW SE Pro Needle Distributor 58mm", "موزع إبر إم إتش دبليو إس إي برو ٥٨ مم", "Achieve flawless, clump-free espresso extractions with the highly advanced MHW SE Pro Needle Distributor. Custom-fitted for 58mm portafilters, this professional WDT (Weiss Distribution Technique) tool utilizes ultra-fine needles to seamlessly break up clumps and evenly distribute coffee grounds throughout the basket. This meticulous preparation prevents channeling, dramatically boosting extraction yield and flavor clarity in every shot.", "احصل على استخلاصات إسبريسو خالية من التكتلات وخالية من العيوب مع موزع الإبر المتقدم للغاية إم إتش دبليو إس إي برو. تم تصميم هذه الأداة الاحترافية لتناسب مقابض الترشيح مقاس ٥٨ مم، وتستخدم إبراً فائقة الدقة لتفتيت التكتلات بسلاسة وتوزيع حبيبات القهوة بالتساوي في جميع أنحاء السلة. يمنع هذا التحضير الدقيق التسرب الجانبي، مما يعزز بشكل كبير من إنتاجية الاستخلاص ووضوح النكهة في كل جرعة.", "Brewing", new[] { ("Space Silver", 566m, 152.82m), ("Obsidian Black", 566m, 152.82m) }),
            ("MHW Coffee Scale Stand", "حامل ميزان قهوة إم إتش دبليو", "Elevate your espresso machine's aesthetics and functionality with the MHW Coffee Scale Stand. Specifically designed to protect your coffee scale from water damage and heat, this sturdy stand provides an elevated platform for precise weighing during extraction. Its durable, water-resistant construction ensures long-lasting performance, while the minimalist black finish seamlessly blends into any modern coffee bar setup.", "ارتقِ بجماليات ووظائف آلة الإسبريسو الخاصة بك مع حامل ميزان القهوة إم إتش دبليو. صُمم هذا الحامل القوي خصيصاً لحماية ميزان القهوة الخاص بك من أضرار المياه والحرارة، ويوفر منصة مرتفعة للوزن الدقيق أثناء الاستخلاص. يضمن هيكله المتين والمقاوم للماء أداءً طويل الأمد، بينما يندمج اللون الأسود البسيط بسلاسة في أي إعداد حديث لركن القهوة.", "Brewing", new[] { ("Black", 195m, 52.65m) }),
            ("MHW RDT Spray Bottle", "بخاخ رذاذ الماء (آر دي تي) إم إتش دبليو", "Reduce static and retention in your coffee grinder with the highly practical MHW RDT Spray Bottle. Designed for the Ross Droplet Technique (RDT), this compact spray bottle delivers a fine, even mist of water onto your coffee beans before grinding. This simple step eliminates static cling, ensuring all your precious coffee grounds end up in the portafilter instead of stuck inside the grinder chute.", "قلل من الكهرباء الساكنة والتطاير في مطحنة القهوة الخاصة بك باستخدام بخاخ رذاذ الماء العملي للغاية من إم إتش دبليو. صُمم هذا البخاخ المدمج لتقنية قطرات روس (RDT)، حيث يطلق رذاذاً دقيقاً ومتساوياً من الماء على حبوب القهوة قبل الطحن. تقضي هذه الخطوة البسيطة على الالتصاق الثابت، مما يضمن وصول جميع حبيبات القهوة الثمينة إلى البورتافلتر بدلاً من أن تعلق داخل مسار المطحنة.", "Brewing", new[] { ("Black", 23m, 6.21m) }),
            ("MHW Coffee Bean Dosing Cup", "وعاء قياس جرعات حبوب القهوة إم إتش دبليو", "Simplify your single-dosing workflow with the sleek MHW Coffee Bean Dosing Cup. Crafted for precision and convenience, this dedicated cup allows you to weigh your coffee beans accurately before grinding. Its wide mouth makes pouring beans out of bags effortless, while its thoughtful design ensures a smooth, spill-free transfer of beans directly into your grinder's hopper.", "قم بتبسيط سير عمل الجرعة الواحدة الخاصة بك مع وعاء قياس جرعات حبوب القهوة الأنيق من إم إتش دبليو. صُنع هذا الوعاء المخصص من أجل الدقة والراحة، ويتيح لك وزن حبوب القهوة بدقة قبل الطحن. فوهته الواسعة تجعل سكب الحبوب من الأكياس أمراً سهلاً، بينما يضمن تصميمه المدروس نقلاً سلساً وخالياً من الانسكاب للحبوب مباشرة إلى وعاء المطحنة.", "Brewing", new[] { ("Black", 47m, 12.69m) }),
            ("MHW Universal Coffee Dosing Ring 58mm", "حلقة جرعات قهوة شاملة إم إتش دبليو ٥٨ مم", "Keep your coffee station pristine and prevent wasted grounds with the MHW 58mm Universal Coffee Dosing Ring. Designed to securely fit most standard 58mm portafilters, this funnel guides all your freshly ground coffee directly into the basket without overflowing. Its smooth interior surface ensures coffee doesn't stick, making your WDT distribution process cleaner and more efficient.", "حافظ على ركن القهوة الخاص بك نقياً وامنع إهدار حبيبات القهوة مع حلقة جرعات القهوة الشاملة مقاس ٥٨ مم من إم إتش دبليو. صُمم هذا القمع ليناسب معظم مقابض الترشيح القياسية مقاس ٥٨ مم بشكل آمن، وهو يوجه كل القهوة المطحونة الطازجة مباشرة إلى السلة دون أن تفيض. يضمن سطحه الداخلي الأملس عدم التصاق القهوة، مما يجعل عملية توزيع القهوة (WDT) أنظف وأكثر كفاءة.", "Brewing", new[] { ("silver", 70m, 18.90m) }),
            ("MHW B-3 Tactics Set 12pcs [UK Plug]", "مجموعة أدوات التكتيك بي-٣ من إم إتش دبليو ١٢ قطعة [قابس بريطاني]", "Equip your professional or home coffee bar with the ultimate barista toolkit: the MHW B-3 Tactics Set. This comprehensive 12-piece bundle includes all the essential, high-quality gear needed for mastering pour-over and espresso preparation, fully customized with a UK plug. Carefully curated for synergy and performance, this all-in-one set provides unmatched value and convenience for the serious coffee enthusiast.", "جهّز ركن القهوة الاحترافي أو المنزلي الخاص بك بمجموعة أدوات الباريستا المثالية: مجموعة التكتيك بي-٣ من إم إتش دبليو. تتضمن هذه الحزمة الشاملة المكونة من ١٢ قطعة جميع المعدات الأساسية عالية الجودة اللازمة لإتقان تحضير القهوة المقطرة والإسبريسو، وهي مخصصة بالكامل بقابس بريطاني. تم تنسيق هذه المجموعة المتكاملة بعناية من أجل التآزر والأداء، وتوفر قيمة وراحة لا مثيل لها لعشاق القهوة الجادين.", "Brewing", new[] { ("Black", 1736m, 468.72m) }),
            ("MHW Smooth Espresso Chilling Ball Stand 2pcs", "حامل كرة تبريد الإسبريسو إم إتش دبليو سموث - قطعتان", "Unlock the full spectrum of your coffee's flavor profile with the MHW Smooth Espresso Chilling Ball Stand set. By pulling your espresso shots directly over these suspended, frozen stainless steel balls, you rapidly drop the temperature, preserving delicate aromatics and reducing bitterness. This 2-piece set allows for efficient back-to-back extractions, making it perfect for experimenting with advanced chilled extraction techniques.", "أطلق العنان لمجموعة كاملة من نكهات قهوتك مع مجموعة حامل كرة تبريد الإسبريسو إم إتش دبليو سموث. من خلال استخلاص جرعات الإسبريسو الخاصة بك مباشرة على كرات الستانلس ستيل المجمدة والمعلقة، يمكنك خفض درجة الحرارة بسرعة، مما يحافظ على العطريات الرقيقة ويقلل من المرارة. تتيح هذه المجموعة المكونة من قطعتين استخلاصات متتالية فعالة، مما يجعلها مثالية لتجربة تقنيات الاستخلاص المبرد المتقدمة.", "Brewing", new[] { ("Black", 125m, 33.75m), ("Silver", 125m, 33.75m) }),
            ("MHW MET Outdoor Cassette Stove", "موقد رحلات غاز إم إتش دبليو ميت", "Brew exceptional coffee anywhere your adventures take you with the MHW MET Outdoor Cassette Stove. Engineered for portability and outdoor resilience, this robust metallic stove delivers a powerful and consistent flame for quickly boiling water or using a Moka pot. Its compact design fits easily into camping gear, ensuring you never have to compromise on the quality of your coffee while enjoying the great outdoors.", "قم بتحضير قهوة استثنائية في أي مكان تأخذك إليه مغامراتك مع موقد الرحلات إم إتش دبليو ميت. تم تصميم هذا الموقد المعدني القوي من أجل سهولة الحمل والمرونة في الهواء الطلق، وهو يوفر لهباً قوياً ومتسقاً لغلي الماء بسرعة أو استخدام وعاء الموكا. يتناسب تصميمه المدمج بسهولة مع معدات التخييم، مما يضمن لك عدم المساومة أبداً على جودة قهوتك أثناء الاستمتاع بالهواء الطلق.", "Brewing", new[] { ("Metal Color", 351m, 94.77m) }),
            ("MHW Gimme French Press 500ml", "أداة فرنش بريس إم إتش دبليو جيمي ٥٠٠ مل", "Experience rich, full-bodied coffee with the elegant MHW Gimme French Press. Boasting a 500ml capacity perfect for sharing, it features a fine-mesh stainless steel filter that expertly extracts essential oils while keeping grounds out of your cup. Its durable, heat-resistant glass carafe is paired with a modern, ergonomic frame, making it a reliable and stylish choice for effortless daily brewing.", "جرب القهوة الغنية والكاملة القوام مع أداة فرنش بريس إم إتش دبليو جيمي الأنيقة. تتميز بسعة ٥٠٠ مل مثالية للمشاركة، وتحتوي على فلتر من الستانلس ستيل ذو شبكة دقيقة يستخلص الزيوت الأساسية بخبرة مع إبقاء الرواسب خارج الكوب. يقترن إبريقها الزجاجي المتين المقاوم للحرارة بإطار حديث ومريح، مما يجعلها خياراً موثوقاً وأنيقاً للتحضير اليومي السهل.", "Brewing", new[] { ("black", 62m, 16.74m), ("white", 62m, 16.74m) }),
            ("MHW Leather Sheath Sealed Canister 600ml", "حاوية تخزين محكمة الغلق بغلاف جلدي إم إتش دبليو ٦٠٠ مل", "Preserve the absolute freshness of your premium coffee beans with the stylish MHW Leather Sheath Sealed Canister. Holding up to 600ml, this airtight storage solution protects your beans from oxygen, moisture, and light—the ultimate enemies of coffee flavor. Encased in a sophisticated leather sheath, it not only ensures maximum freshness but also adds a luxurious, tactile element to your coffee bar decor.", "حافظ على النضارة المطلقة لحبوب القهوة الفاخرة الخاصة بك مع حاوية إم إتش دبليو الأنيقة المحكمة الغلق ذات الغلاف الجلدي. تستوعب هذه الحاوية ما يصل إلى ٦٠٠ مل، وتحمي حبوبك من الأكسجين والرطوبة والضوء - الأعداء اللدودين لنكهة القهوة. وهي مغلفة بغمد جلدي راقٍ، ولا تضمن فقط أقصى قدر من النضارة ولكنها تضيف أيضاً عنصراً فاخراً وملموساً إلى ديكور ركن القهوة الخاص بك.", "Accessories", new[] { ("N/A", 62m, 16.74m) }),
            ("MHW Energy Coffee Beans Storage Tubes Set (20-26g x 8)", "مجموعة أنابيب تخزين حبوب القهوة إم إتش دبليو إنرجي (٢٠-٢٦ غرام × ٨)", "Streamline your single-dosing routine with the MHW Energy Coffee Beans Storage Tubes Set. This premium set includes 8 individual sealed tubes, each perfectly sized to hold a 20-26g dose of coffee beans. By pre-weighing your daily doses and sealing them away from air exposure, you lock in peak freshness and significantly speed up your morning workflow. The elegant display stand keeps your coffee station beautifully organized.", "قم بتبسيط روتين الجرعة الواحدة الخاص بك مع مجموعة أنابيب تخزين حبوب القهوة إم إتش دبليو إنرجي. تتضمن هذه المجموعة الفاخرة ٨ أنابيب فردية محكمة الغلق، كل منها بحجم مثالي لاستيعاب جرعة ٢٠-٢٦ غرام من حبوب القهوة. من خلال وزن جرعاتك اليومية مسبقاً وإغلاقها بعيداً عن التعرض للهواء، فإنك تحبس النضارة القصوى وتسرع بشكل كبير من سير عملك الصباحي. يحافظ حامل العرض الأنيق على ركن القهوة الخاص بك منظماً بشكل جميل.", "Accessories", new[] { ("N/A", 293m, 79.11m) }),
            ("MHW Air Blower", "منفاخ هواء إم إتش دبليو", "Effectively remove loose coffee grounds and dust from your grinder and coffee station with the MHW Air Blower. Made from durable, food-safe silicone, this handy tool delivers a strong puff of air to clean hard-to-reach crevices without the need for brushes or water. It is an essential, gentle cleaning accessory for maintaining the pristine condition of your delicate coffee equipment.", "قم بإزالة حبيبات القهوة المتناثرة والغبار بفعالية من المطحنة وركن القهوة الخاص بك باستخدام منفاخ هواء إم إتش دبليو. صُنعت هذه الأداة اليدوية من السيليكون المتين والآمن غذائياً، وتوفر نفخة هواء قوية لتنظيف الشقوق التي يصعب الوصول إليها دون الحاجة إلى فرش أو ماء. إنها ملحق تنظيف لطيف وأساسي للحفاظ على الحالة النقية لمعدات القهوة الحساسة الخاصة بك.", "Cleaning & Maintenance", new[] { ("silicone", 47m, 12.69m) }),
            ("MHW Coffee Bar Brush", "فرشاة تنظيف ركن القهوة إم إتش دبليو", "Keep your workspace spotless and professional with the MHW Coffee Bar Brush. Designed with dense, soft bristles, it effortlessly sweeps away stray coffee grounds, dust, and debris from your countertop and espresso machine without scratching surfaces. The ergonomic black handle ensures a comfortable grip, making daily cleanup quick and efficient for any barista.", "حافظ على مساحة عملك نظيفة واحترافية مع فرشاة تنظيف ركن القهوة إم إتش دبليو. مُصممة بشعيرات كثيفة وناعمة، وتزيل بسهولة حبيبات القهوة المتناثرة والغبار والحطام من سطح العمل وآلة الإسبريسو دون خدش الأسطح. يضمن المقبض الأسود المريح قبضة محكمة، مما يجعل التنظيف اليومي سريعاً وفعالاً لأي باريستا.", "Cleaning & Maintenance", new[] { ("Black", 78m, 21.06m) }),
            ("MHW Long Measuring Spoon Stainless Steel 8g", "ملعقة قياس طويلة من الستانلس ستيل إم إتش دبليو ٨ غرام", "Ensure precise and consistent dosing with the MHW Long Measuring Spoon. Crafted from high-quality stainless steel, this durable spoon accurately scoops 8g of coffee beans or grounds. Its elongated handle is specifically designed to reach deep into tall coffee bags or storage canisters with ease, making it a highly practical tool for your daily brewing routine.", "اضبط الجرعات بدقة واتساق مع ملعقة القياس الطويلة من إم إتش دبليو. هذه الملعقة المتينة مصنوعة من الستانلس ستيل عالي الجودة، وتغرف ٨ غرامات من حبوب القهوة أو البن المطحون بدقة. صُمم مقبضها الطويل خصيصاً للوصول إلى عمق أكياس القهوة الطويلة أو حاويات التخزين بسهولة، مما يجعلها أداة عملية للغاية لروتين التحضير اليومي.", "Brewing", new[] { ("silver spot", 47m, 12.69m) }),
            ("MHW Coffee Spoon Stainless Steel", "ملعقة قهوة من الستانلس ستيل إم إتش دبليو", "Stir and enjoy your freshly brewed coffee with the elegant MHW Stainless Steel Coffee Spoon. Made from premium, rust-resistant stainless steel, this spoon offers durability and a sleek aesthetic that complements any coffee cup or saucer. Its perfectly balanced design makes it ideal for stirring espresso, lattes, or tasting your favorite brews.", "حرك واستمتع بقهوتك الطازجة مع ملعقة القهوة الأنيقة من الستانلس ستيل من إم إتش دبليو. مصنوعة من الستانلس ستيل الفاخر والمقاوم للصدأ، وتوفر هذه الملعقة متانة وجمالية أنيقة تكمل أي كوب أو صحن قهوة. تصميمها المتوازن تماماً يجعلها مثالية لتحريك الإسبريسو أو اللاتيه أو تذوق مشروباتك المفضلة.", "Brewing", new[] { ("silver spot", 23m, 6.21m) }),
            ("MHW Coffee Extraction Observation Mirror", "مرآة مراقبة استخلاص القهوة إم إتش دبليو", "Monitor your espresso shots effortlessly with the MHW Coffee Extraction Observation Mirror. Designed to sit securely on your drip tray, this adjustable mirror provides a clear, magnified view of your bottomless portafilter. It allows you to visually diagnose your puck preparation in real-time, helping you identify channeling and perfect your extraction without bending down.", "راقب جرعات الإسبريسو الخاصة بك بسهولة باستخدام مرآة مراقبة استخلاص القهوة إم إتش دبليو. صُممت هذه المرآة القابلة للتعديل لتستقر بأمان على صينية التقطير، وتوفر رؤية واضحة ومكبرة للبورتافلتر المكشوف. تتيح لك تشخيص تحضير قرص القهوة بصرياً في الوقت الفعلي، مما يساعدك على تحديد التسرب الجانبي وإتقان الاستخلاص دون الانحناء.", "Brewing", new[] { ("Black", 94m, 25.38m) }),
        };

        var products = new List<Product>();
        foreach (var item in accessories)
        {
            var slug = item.NameEn.ToLower()
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
                    new() { LanguageCode = "en", Name = item.NameEn, Description = item.DescriptionEn },
                    new() { LanguageCode = "ar", Name = item.NameAr, Description = item.DescriptionAr }
                },
                Variants = variants
            });
        }

        await context.Products.AddRangeAsync(products);
        await TrySaveAsync(context, logger, "Accessories");
        logger.LogInformation("MHW Accessories seeded successfully: {Count} products.", products.Count);
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

            // Saudi Arabia
            var saCountry = countries.FirstOrDefault(c => c.Code == "SA");
            if (saCountry != null)
            {
                cities.AddRange(new[]
                {
                    new City { Name = "Riyadh",      CountryId = saCountry.Id },
                    new City { Name = "Jeddah",      CountryId = saCountry.Id },
                    new City { Name = "Mecca",       CountryId = saCountry.Id },
                    new City { Name = "Medina",      CountryId = saCountry.Id },
                    new City { Name = "Dammam",      CountryId = saCountry.Id },
                    new City { Name = "Khobar",      CountryId = saCountry.Id },
                    new City { Name = "Dhahran",     CountryId = saCountry.Id },
                    new City { Name = "Taif",        CountryId = saCountry.Id },
                    new City { Name = "Tabuk",       CountryId = saCountry.Id },
                    new City { Name = "Abha",        CountryId = saCountry.Id },
                });
            }

            // United Arab Emirates
            var aeCountry = countries.FirstOrDefault(c => c.Code == "AE");
            if (aeCountry != null)
            {
                cities.AddRange(new[]
                {
                    new City { Name = "Abu Dhabi",     CountryId = aeCountry.Id },
                    new City { Name = "Dubai",         CountryId = aeCountry.Id },
                    new City { Name = "Sharjah",       CountryId = aeCountry.Id },
                    new City { Name = "Ajman",         CountryId = aeCountry.Id },
                    new City { Name = "Umm Al Quwain", CountryId = aeCountry.Id },
                    new City { Name = "Ras Al Khaimah",CountryId = aeCountry.Id },
                    new City { Name = "Fujairah",      CountryId = aeCountry.Id },
                });
            }

            // Qatar
            var qaCountry = countries.FirstOrDefault(c => c.Code == "QA");
            if (qaCountry != null)
            {
                cities.AddRange(new[]
                {
                    new City { Name = "Doha",           CountryId = qaCountry.Id },
                    new City { Name = "Al Rayyan",      CountryId = qaCountry.Id },
                    new City { Name = "Al Wakrah",      CountryId = qaCountry.Id },
                    new City { Name = "Al Khor",        CountryId = qaCountry.Id },
                    new City { Name = "Umm Salal",      CountryId = qaCountry.Id },
                    new City { Name = "Al Shamal",      CountryId = qaCountry.Id },
                });
            }

            // Kuwait
            var kwCountry = countries.FirstOrDefault(c => c.Code == "KW");
            if (kwCountry != null)
            {
                cities.AddRange(new[]
                {
                    new City { Name = "Al Asimah (Capital)", CountryId = kwCountry.Id },
                    new City { Name = "Hawalli",              CountryId = kwCountry.Id },
                    new City { Name = "Farwaniya",            CountryId = kwCountry.Id },
                    new City { Name = "Ahmadi",               CountryId = kwCountry.Id },
                    new City { Name = "Jahra",                CountryId = kwCountry.Id },
                    new City { Name = "Mubarak Al-Kabeer",    CountryId = kwCountry.Id },
                });
            }

            // Bahrain
            var bhCountry = countries.FirstOrDefault(c => c.Code == "BH");
            if (bhCountry != null)
            {
                cities.AddRange(new[]
                {
                    new City { Name = "Manama",         CountryId = bhCountry.Id },
                    new City { Name = "Riffa",          CountryId = bhCountry.Id },
                    new City { Name = "Muharraq",       CountryId = bhCountry.Id },
                    new City { Name = "Hamad Town",     CountryId = bhCountry.Id },
                    new City { Name = "Isa Town",       CountryId = bhCountry.Id },
                    new City { Name = "Sitra",          CountryId = bhCountry.Id },
                });
            }

            // Oman
            var omCountry = countries.FirstOrDefault(c => c.Code == "OM");
            if (omCountry != null)
            {
                cities.AddRange(new[]
                {
                    new City { Name = "Muscat",         CountryId = omCountry.Id },
                    new City { Name = "Salalah",        CountryId = omCountry.Id },
                    new City { Name = "Sohar",          CountryId = omCountry.Id },
                    new City { Name = "Nizwa",          CountryId = omCountry.Id },
                    new City { Name = "Sur",            CountryId = omCountry.Id },
                    new City { Name = "Ibri",           CountryId = omCountry.Id },
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

        // Seed Country Arabic Translations
        if (!await context.CountryTranslations.AnyAsync())
        {
            var countries = await context.Countries.ToListAsync();
            var translations = new List<CountryTranslation>();

            var countryArNames = new Dictionary<string, string>
            {
                { "SA", "المملكة العربية السعودية" },
                { "AE", "الإمارات العربية المتحدة" },
                { "QA", "قطر" },
                { "KW", "الكويت" },
                { "BH", "البحرين" },
                { "OM", "عُمان" },
            };

            foreach (var country in countries)
            {
                if (countryArNames.TryGetValue(country.Code, out var arName))
                    translations.Add(new CountryTranslation { CountryId = country.Id, LanguageCode = "ar", TranslatedName = arName });

                translations.Add(new CountryTranslation { CountryId = country.Id, LanguageCode = "en", TranslatedName = country.Name });
            }

            await context.CountryTranslations.AddRangeAsync(translations);
            await TrySaveAsync(context, logger, "CountryTranslations");
            logger.LogInformation("CountryTranslations seeded: {Count} rows added", translations.Count);
        }

        // Seed City Arabic Translations
        if (!await context.CityTranslations.AnyAsync())
        {
            var cities = await context.Cities.Include(c => c.Country).ToListAsync();
            var translations = new List<CityTranslation>();

            var cityArNames = new Dictionary<string, string>
            {
                // Saudi Arabia
                { "Riyadh",               "الرياض" },
                { "Jeddah",               "جدة" },
                { "Mecca",                "مكة المكرمة" },
                { "Medina",               "المدينة المنورة" },
                { "Dammam",               "الدمام" },
                { "Khobar",               "الخبر" },
                { "Dhahran",              "الظهران" },
                { "Taif",                 "الطائف" },
                { "Tabuk",                "تبوك" },
                { "Abha",                 "أبها" },
                // UAE
                { "Abu Dhabi",            "أبوظبي" },
                { "Dubai",                "دبي" },
                { "Sharjah",              "الشارقة" },
                { "Ajman",                "عجمان" },
                { "Umm Al Quwain",        "أم القيوين" },
                { "Ras Al Khaimah",       "رأس الخيمة" },
                { "Fujairah",             "الفجيرة" },
                // Qatar
                { "Doha",                 "الدوحة" },
                { "Al Rayyan",            "الريان" },
                { "Al Wakrah",            "الوكرة" },
                { "Al Khor",              "الخور" },
                { "Umm Salal",            "أم صلال" },
                { "Al Shamal",            "الشمال" },
                // Kuwait
                { "Al Asimah (Capital)",  "العاصمة" },
                { "Hawalli",              "حولي" },
                { "Farwaniya",            "الفروانية" },
                { "Ahmadi",               "الأحمدي" },
                { "Jahra",                "الجهراء" },
                { "Mubarak Al-Kabeer",    "مبارك الكبير" },
                // Bahrain
                { "Manama",               "المنامة" },
                { "Riffa",                "الرفاع" },
                { "Muharraq",             "المحرق" },
                { "Hamad Town",           "مدينة حمد" },
                { "Isa Town",             "مدينة عيسى" },
                { "Sitra",                "سترة" },
                // Oman
                { "Muscat",               "مسقط" },
                { "Salalah",              "صلالة" },
                { "Sohar",                "صحار" },
                { "Nizwa",                "نزوى" },
                { "Sur",                  "صور" },
                { "Ibri",                 "عبري" },
            };

            foreach (var city in cities)
            {
                if (cityArNames.TryGetValue(city.Name, out var arName))
                    translations.Add(new CityTranslation { CityId = city.Id, LanguageCode = "ar", TranslatedName = arName });

                translations.Add(new CityTranslation { CityId = city.Id, LanguageCode = "en", TranslatedName = city.Name });
            }

            await context.CityTranslations.AddRangeAsync(translations);
            await TrySaveAsync(context, logger, "CityTranslations");
            logger.LogInformation("CityTranslations seeded: {Count} rows added", translations.Count);
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
        var category = await context.Categories.FirstOrDefaultAsync(c => c.Slug == "electric");
        var brand = await context.Brands.FirstOrDefaultAsync(b => b.Name == "Fiorenzato");

        if (category == null)
            throw new InvalidOperationException("Seeding prerequisite missing: Category with slug 'electric' was not found. Ensure SeedCategoriesAsync completed successfully before SeedFiorenzatoGrindersAsync.");

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
