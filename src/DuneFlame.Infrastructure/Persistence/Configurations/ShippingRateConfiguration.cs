using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class ShippingRateConfiguration : IEntityTypeConfiguration<ShippingRate>
{
    public void Configure(EntityTypeBuilder<ShippingRate> builder)
    {
        builder.Property(sr => sr.Currency).HasConversion<string>();
        builder.Property(sr => sr.Cost).HasPrecision(18, 2);
        builder.HasIndex(sr => new { sr.CountryId, sr.Currency }).IsUnique();
    }
}
