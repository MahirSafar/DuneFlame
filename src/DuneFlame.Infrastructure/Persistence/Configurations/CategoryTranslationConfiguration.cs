using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class CategoryTranslationConfiguration : IEntityTypeConfiguration<CategoryTranslation>
{
    public void Configure(EntityTypeBuilder<CategoryTranslation> builder)
    {
        builder.HasKey(ct => ct.Id);
        builder.Property(ct => ct.LanguageCode).IsRequired().HasMaxLength(5);
        builder.Property(ct => ct.Name).IsRequired().HasMaxLength(500);

        builder.HasOne(ct => ct.Category)
            .WithMany(c => c.Translations)
            .HasForeignKey(ct => ct.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ct => new { ct.CategoryId, ct.LanguageCode }).IsUnique();
    }
}
