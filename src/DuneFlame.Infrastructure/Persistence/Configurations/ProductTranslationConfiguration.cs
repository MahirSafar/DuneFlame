using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class ProductTranslationConfiguration : IEntityTypeConfiguration<ProductTranslation>
{
    public void Configure(EntityTypeBuilder<ProductTranslation> builder)
    {
        builder.HasKey(pt => pt.Id);
        builder.Property(pt => pt.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(pt => pt.Name).IsRequired().HasMaxLength(500);
        builder.Property(pt => pt.Description).IsRequired().HasColumnType("text");

        builder.HasOne(pt => pt.Product)
            .WithMany(p => p.Translations)
            .HasForeignKey(pt => pt.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pt => new { pt.ProductId, pt.LanguageCode }).IsUnique();
    }
}
