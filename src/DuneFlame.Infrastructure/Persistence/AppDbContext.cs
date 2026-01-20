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
    public DbSet<Category> Categories { get; set; }
    public DbSet<Origin> Origins { get; set; }
    public DbSet<ProductImage> ProductImages { get; set; }
    public DbSet<Slider> Sliders { get; set; }
    public DbSet<AboutSection> AboutSections { get; set; }
    public DbSet<Cart> Carts { get; set; }
    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<RewardWallet> RewardWallets { get; set; }
    public DbSet<RewardTransaction> RewardTransactions { get; set; }
    public DbSet<PaymentTransaction> PaymentTransactions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Cədvəl adlarını "AspNetUsers" yerinə daha səliqəli edə bilərik (İstəyə bağlı)
        modelBuilder.Entity<ApplicationUser>().ToTable("Users");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalLogin>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LoginProvider).IsRequired();
            entity.Property(e => e.ProviderKey).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany(u => u.ExternalLogins)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired();
            entity.HasOne(e => e.User)
                .WithMany(u => u.PasswordResetTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserProfile 1-to-1 Relationship
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasOne(p => p.User)
                .WithOne() // ApplicationUser tərəfində naviqasiya property-si mütləq deyil
                .HasForeignKey<UserProfile>(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Newsletter Unique Email
        modelBuilder.Entity<NewsletterSubscription>()
            .HasIndex(n => n.Email)
            .IsUnique();

        modelBuilder.Entity<Product>()
        .HasOne(p => p.Category)
        .WithMany(c => c.Products)
        .HasForeignKey(p => p.CategoryId)
        .OnDelete(DeleteBehavior.Restrict); // Kateqoriya silinərsə məhsulları silmə (xəta ver)

        // Product - Origin Relationship
        modelBuilder.Entity<Product>()
            .HasOne(p => p.Origin)
            .WithMany(o => o.Products)
            .HasForeignKey(p => p.OriginId)
            .OnDelete(DeleteBehavior.SetNull); // Origin silinərsə OriginId-ni null-a çevir

        // Product - Images Relationship
        modelBuilder.Entity<ProductImage>()
            .HasOne(i => i.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade); // Məhsul silinərsə şəkillərini də sil

        // Precision for Price (PostgreSQL üçün vacibdir)
        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Product>()
            .Property(p => p.DiscountPercentage)
            .HasPrecision(18, 2);

        // Product Slug Configuration
        modelBuilder.Entity<Product>()
            .Property(p => p.Slug)
            .IsRequired()
            .HasMaxLength(255);

        modelBuilder.Entity<Product>()
            .HasIndex(p => p.Slug)
            .IsUnique();

        // Cart - User Relationship (1-to-Many)
        modelBuilder.Entity<Cart>()
            .HasOne(c => c.ApplicationUser)
            .WithMany(u => u.Carts)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // CartItem - Cart Relationship (1-to-Many)
        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(ci => ci.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        // CartItem - Product Relationship (1-to-Many)
        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.Product)
            .WithMany()
            .HasForeignKey(ci => ci.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // CartItem Quantity Constraint
        modelBuilder.Entity<CartItem>()
            .Property(ci => ci.Quantity)
            .HasDefaultValue(1);

        // Order - User Relationship (1-to-Many)
        modelBuilder.Entity<Order>()
            .HasOne(o => o.ApplicationUser)
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Order RowVersion - Optimistic Concurrency Control
        modelBuilder.Entity<Order>()
            .Property(o => o.RowVersion)
            .IsRowVersion();

        // Order - TotalAmount Precision
        modelBuilder.Entity<Order>()
            .Property(o => o.TotalAmount)
            .HasPrecision(18, 2);

        // OrderItem - Order Relationship (1-to-Many)
        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // OrderItem - UnitPrice Precision
        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.UnitPrice)
            .HasPrecision(18, 2);

        // RewardWallet - User Relationship (1-to-1)
        modelBuilder.Entity<RewardWallet>()
            .HasOne(w => w.ApplicationUser)
            .WithOne(u => u.RewardWallet)
            .HasForeignKey<RewardWallet>(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // RewardWallet Balance Precision
        modelBuilder.Entity<RewardWallet>()
            .Property(w => w.Balance)
            .HasPrecision(18, 2);

        // RewardTransaction - Wallet Relationship (1-to-Many)
        modelBuilder.Entity<RewardTransaction>()
            .HasOne(rt => rt.RewardWallet)
            .WithMany(w => w.Transactions)
            .HasForeignKey(rt => rt.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        // RewardTransaction Amount Precision
        modelBuilder.Entity<RewardTransaction>()
            .Property(rt => rt.Amount)
            .HasPrecision(18, 2);

        // Order Points Precision
        modelBuilder.Entity<Order>()
            .Property(o => o.PointsRedeemed)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.PointsEarned)
            .HasPrecision(18, 2);

        // PaymentTransaction - Order Relationship (1-to-Many)
        modelBuilder.Entity<PaymentTransaction>()
            .HasOne(pt => pt.Order)
            .WithMany(o => o.PaymentTransactions)
            .HasForeignKey(pt => pt.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // PaymentTransaction RefundId - Idempotency tracking for refunds (explicitly optional)
        modelBuilder.Entity<PaymentTransaction>()
            .Property(pt => pt.RefundId)
            .IsRequired(false)
            .HasMaxLength(255);

        // PaymentTransaction Amount Precision
        modelBuilder.Entity<PaymentTransaction>()
            .Property(pt => pt.Amount)
            .HasPrecision(18, 2);
    }
}
