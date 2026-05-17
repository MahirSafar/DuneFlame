using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class CustomerBasketConfiguration : IEntityTypeConfiguration<CustomerBasket>
{
    public void Configure(EntityTypeBuilder<CustomerBasket> builder)
    {
        builder.HasKey(e => e.Id);
        builder.ToTable("CustomerBaskets");
        builder.IsUnlogged();
        builder.Property(e => e.Items).HasColumnType("jsonb");
    }
}
