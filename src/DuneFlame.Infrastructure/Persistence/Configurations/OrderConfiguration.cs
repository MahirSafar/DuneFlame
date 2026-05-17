using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.CurrencyCode)
            .HasConversion<string>()
            .HasDefaultValue(Currency.USD);

        builder.Property(o => o.TotalAmount).HasPrecision(18, 2);
        builder.Property(o => o.PointsRedeemed).HasPrecision(18, 2);
        builder.Property(o => o.PointsEarned).HasPrecision(18, 2);
        builder.Property(o => o.RowVersion).IsRowVersion();

        builder.HasOne(o => o.ApplicationUser)
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
