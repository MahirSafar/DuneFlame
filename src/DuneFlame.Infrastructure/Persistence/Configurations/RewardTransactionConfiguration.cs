using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class RewardTransactionConfiguration : IEntityTypeConfiguration<RewardTransaction>
{
    public void Configure(EntityTypeBuilder<RewardTransaction> builder)
    {
        builder.Property(rt => rt.Amount).HasPrecision(18, 2);

        builder.HasOne(rt => rt.RewardWallet)
            .WithMany(w => w.Transactions)
            .HasForeignKey(rt => rt.WalletId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
