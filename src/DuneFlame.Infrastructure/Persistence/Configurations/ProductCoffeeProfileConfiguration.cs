using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class ProductCoffeeProfileConfiguration : IEntityTypeConfiguration<ProductCoffeeProfile>
{
    public void Configure(EntityTypeBuilder<ProductCoffeeProfile> builder)
    {
        builder.HasIndex(p => p.ProductId).IsUnique();

        builder.HasOne(cp => cp.Product)
            .WithOne(p => p.CoffeeProfile)
            .HasForeignKey<ProductCoffeeProfile>(cp => cp.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Origin)
            .WithMany(o => o.Profiles)
            .HasForeignKey(p => p.OriginId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.RoastLevels)
            .WithMany(r => r.Profiles)
            .UsingEntity(j => j.ToTable("ProductCoffeeProfileRoastLevels"));

        builder.HasMany(p => p.GrindTypes)
            .WithMany(g => g.Profiles)
            .UsingEntity(j => j.ToTable("ProductCoffeeProfileGrindTypes"));
    }
}
