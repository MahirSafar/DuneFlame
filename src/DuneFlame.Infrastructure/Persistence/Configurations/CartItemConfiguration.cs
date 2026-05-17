using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.Property(ci => ci.Quantity).HasDefaultValue(1);

        builder.HasOne(ci => ci.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(ci => ci.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ci => ci.ProductVariant)
            .WithMany()
            .HasForeignKey(ci => ci.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ci => ci.RoastLevel)
            .WithMany()
            .HasForeignKey(ci => ci.RoastLevelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ci => ci.GrindType)
            .WithMany()
            .HasForeignKey(ci => ci.GrindTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
