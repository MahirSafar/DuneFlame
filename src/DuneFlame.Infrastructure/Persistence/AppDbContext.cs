using DuneFlame.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Product> Products { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<ExternalLogin> ExternalLogins { get; set; }
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<NewsletterSubscription> NewsletterSubscriptions { get; set; }
    public DbSet<ContactMessage> ContactMessages { get; set; }

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
    }
}
