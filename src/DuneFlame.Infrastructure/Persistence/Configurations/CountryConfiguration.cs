using DuneFlame.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.Code).IsRequired().HasMaxLength(2);
        builder.HasIndex(c => c.Code).IsUnique();

        builder.HasMany(c => c.Cities)
            .WithOne(c => c.Country)
            .HasForeignKey(c => c.CountryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.ShippingRates)
            .WithOne(sr => sr.Country)
            .HasForeignKey(sr => sr.CountryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Translations)
            .WithOne(t => t.Country)
            .HasForeignKey(t => t.CountryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
