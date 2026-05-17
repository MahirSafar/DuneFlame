using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class SliderConfiguration : IEntityTypeConfiguration<Slider>
{
    public void Configure(EntityTypeBuilder<Slider> builder)
    {
        builder.Property(s => s.ImageUrl).IsRequired();
        builder.Property(s => s.Order).HasDefaultValue(0);
        builder.Property(s => s.IsActive).HasDefaultValue(true);

        builder.HasMany(s => s.Translations)
            .WithOne(st => st.Slider)
            .HasForeignKey(st => st.SliderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
