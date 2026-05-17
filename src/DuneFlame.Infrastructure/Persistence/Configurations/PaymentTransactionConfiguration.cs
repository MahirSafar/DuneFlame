using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.Property(pt => pt.CurrencyCode)
            .HasConversion<string>()
            .HasDefaultValue(Currency.USD);

        builder.Property(pt => pt.Amount).HasPrecision(18, 2);
        builder.Property(pt => pt.RefundId).IsRequired(false).HasMaxLength(255);

        builder.HasOne(pt => pt.Order)
            .WithMany(o => o.PaymentTransactions)
            .HasForeignKey(pt => pt.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
