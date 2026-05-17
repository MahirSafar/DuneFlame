using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class ProductEquipmentProfileConfiguration : IEntityTypeConfiguration<ProductEquipmentProfile>
{
    public void Configure(EntityTypeBuilder<ProductEquipmentProfile> builder)
    {
        builder.HasIndex(ep => ep.ProductId).IsUnique();

        builder.HasOne(ep => ep.Product)
            .WithOne(p => p.EquipmentProfile)
            .HasForeignKey<ProductEquipmentProfile>(ep => ep.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.Specifications)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, string>()
            )
            .Metadata.SetValueComparer(
                new ValueComparer<Dictionary<string, string>>(
                    (c1, c2) => c1 != null && c2 != null && c1.Count == c2.Count && !c1.Except(c2).Any(),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.Key.GetHashCode(), v.Value.GetHashCode())),
                    c => c.ToDictionary(kv => kv.Key, kv => kv.Value)
                )
            );
    }
}
