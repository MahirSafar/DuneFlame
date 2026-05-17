using DuneFlame.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<ExternalLogin> ExternalLogins { get; set; }
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<NewsletterSubscription> NewsletterSubscriptions { get; set; }
    public DbSet<ContactMessage> ContactMessages { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<ProductTranslation> ProductTranslations { get; set; }
    public DbSet<FlavourNote> FlavourNotes { get; set; }
    public DbSet<FlavourNoteTranslation> FlavourNoteTranslations { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<CategoryTranslation> CategoryTranslations { get; set; }
    public DbSet<Origin> Origins { get; set; }
    public DbSet<ProductImage> ProductImages { get; set; }
    public DbSet<RoastLevelEntity> RoastLevels { get; set; }
    public DbSet<GrindType> GrindTypes { get; set; }
    public DbSet<AboutSection> AboutSections { get; set; }
    public DbSet<Cart> Carts { get; set; }
    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<RewardWallet> RewardWallets { get; set; }
    public DbSet<RewardTransaction> RewardTransactions { get; set; }
    public DbSet<PaymentTransaction> PaymentTransactions { get; set; }
    public DbSet<Country> Countries { get; set; }
    public DbSet<CountryTranslation> CountryTranslations { get; set; }
    public DbSet<City> Cities { get; set; }
    public DbSet<CityTranslation> CityTranslations { get; set; }
    public DbSet<ShippingRate> ShippingRates { get; set; }
    public DbSet<Slider> Sliders { get; set; }
    public DbSet<SliderTranslation> SliderTranslations { get; set; }
    public DbSet<CustomerBasket> CustomerBaskets { get; set; }
    public DbSet<ProductCoffeeProfile> ProductCoffeeProfiles { get; set; }
    public DbSet<ProductVariant> ProductVariants { get; set; }
    public DbSet<ProductVariantPrice> ProductVariantPrices { get; set; }
    public DbSet<ProductAttribute> ProductAttributes { get; set; }
    public DbSet<ProductAttributeTranslation> ProductAttributeTranslations { get; set; }
    public DbSet<ProductAttributeValue> ProductAttributeValues { get; set; }
    public DbSet<ProductAttributeValueTranslation> ProductAttributeValueTranslations { get; set; }
    public DbSet<ProductVariantOption> ProductVariantOptions { get; set; }
    public DbSet<ProductEquipmentProfile> ProductEquipmentProfiles { get; set; }
    public DbSet<Brand> Brands { get; set; }
    public DbSet<BrandTranslation> BrandTranslations { get; set; }
    public DbSet<WholesaleLead> WholesaleLeads { get; set; }
    public DbSet<GrindTypeTranslation> GrindTypeTranslations { get; set; }
    public DbSet<RoastLevelTranslation> RoastLevelTranslations { get; set; }
    public DbSet<OriginTranslation> OriginTranslations { get; set; }
    public DbSet<AboutSectionTranslation> AboutSectionTranslations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>().ToTable("Users");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
