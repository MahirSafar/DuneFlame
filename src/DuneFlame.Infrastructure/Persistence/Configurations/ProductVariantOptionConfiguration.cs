using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class ProductVariantOptionConfiguration : IEntityTypeConfiguration<ProductVariantOption>
{
    public void Configure(EntityTypeBuilder<ProductVariantOption> builder)
    {
        builder.HasOne(o => o.ProductAttributeValue)
            .WithMany()
            .HasForeignKey(o => o.ProductAttributeValueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
