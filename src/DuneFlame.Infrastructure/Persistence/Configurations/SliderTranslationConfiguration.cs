using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class SliderTranslationConfiguration : IEntityTypeConfiguration<SliderTranslation>
{
    public void Configure(EntityTypeBuilder<SliderTranslation> builder)
    {
        builder.HasKey(st => st.Id);
        builder.Property(st => st.LanguageCode).IsRequired().HasMaxLength(5);
        builder.Property(st => st.Title).IsRequired().HasMaxLength(500);
        builder.Property(st => st.Subtitle).HasMaxLength(1000);
        builder.Property(st => st.ButtonText).HasMaxLength(100);
        builder.HasIndex(st => new { st.SliderId, st.LanguageCode }).IsUnique();
    }
}
