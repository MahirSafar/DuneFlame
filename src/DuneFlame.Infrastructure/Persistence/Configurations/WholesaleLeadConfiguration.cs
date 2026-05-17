using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class WholesaleLeadConfiguration : IEntityTypeConfiguration<WholesaleLead>
{
    public void Configure(EntityTypeBuilder<WholesaleLead> builder)
    {
        builder.Property(w => w.FullName).IsRequired().HasMaxLength(100);
        builder.Property(w => w.BusinessName).IsRequired().HasMaxLength(150);
        builder.Property(w => w.Email).IsRequired().HasMaxLength(200);
        builder.Property(w => w.Phone).IsRequired().HasMaxLength(30);
        builder.Property(w => w.Message).HasMaxLength(1000);
        builder.Property(w => w.BusinessType).HasConversion<string>();
        builder.Property(w => w.MonthlyVolume).HasConversion<string>();
        builder.Property(w => w.IsReviewed).HasDefaultValue(false);
    }
}
