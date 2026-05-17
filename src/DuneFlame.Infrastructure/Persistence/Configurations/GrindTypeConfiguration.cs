using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class GrindTypeConfiguration : IEntityTypeConfiguration<GrindType>
{
    public void Configure(EntityTypeBuilder<GrindType> builder)
    {
        builder.Property(g => g.Name).IsRequired().HasMaxLength(100);
    }
}
