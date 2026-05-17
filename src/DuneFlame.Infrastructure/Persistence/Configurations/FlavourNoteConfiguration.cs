using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class FlavourNoteConfiguration : IEntityTypeConfiguration<FlavourNote>
{
    public void Configure(EntityTypeBuilder<FlavourNote> builder)
    {
        builder.Property(fn => fn.Name).IsRequired().HasMaxLength(100);
        builder.Property(fn => fn.DisplayOrder).HasDefaultValue(0);

        builder.HasOne(fn => fn.ProductCoffeeProfile)
            .WithMany(p => p.FlavourNotes)
            .HasForeignKey(fn => fn.ProductCoffeeProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
