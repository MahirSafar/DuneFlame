using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class FlavourNoteTranslationConfiguration : IEntityTypeConfiguration<FlavourNoteTranslation>
{
    public void Configure(EntityTypeBuilder<FlavourNoteTranslation> builder)
    {
        builder.Property(fnt => fnt.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(fnt => fnt.Name).IsRequired().HasMaxLength(100);

        builder.HasOne(fnt => fnt.FlavourNote)
            .WithMany(fn => fn.Translations)
            .HasForeignKey(fnt => fnt.FlavourNoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(fnt => new { fnt.FlavourNoteId, fnt.LanguageCode }).IsUnique();
    }
}
