using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class ProductVariantPriceConfiguration : IEntityTypeConfiguration<ProductVariantPrice>
{
    public void Configure(EntityTypeBuilder<ProductVariantPrice> builder)
    {
        builder.Property(p => p.Price).HasPrecision(18, 2);
        builder.Property(p => p.Currency).HasConversion<string>();

        builder.HasOne(pv => pv.ProductVariant)
            .WithMany(v => v.Prices)
            .HasForeignKey(pv => pv.ProductVariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
