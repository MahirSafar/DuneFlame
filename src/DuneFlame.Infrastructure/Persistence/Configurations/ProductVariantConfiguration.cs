using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.HasIndex(v => v.Sku).IsUnique();

        builder.HasMany(pv => pv.Options)
            .WithOne(o => o.ProductVariant)
            .HasForeignKey(o => o.ProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
