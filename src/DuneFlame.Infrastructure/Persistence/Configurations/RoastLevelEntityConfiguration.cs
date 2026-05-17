using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class RoastLevelEntityConfiguration : IEntityTypeConfiguration<RoastLevelEntity>
{
    public void Configure(EntityTypeBuilder<RoastLevelEntity> builder)
    {
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
    }
}
