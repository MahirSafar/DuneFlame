using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuneFlame.Infrastructure.Persistence.Configurations;

public class RewardWalletConfiguration : IEntityTypeConfiguration<RewardWallet>
{
    public void Configure(EntityTypeBuilder<RewardWallet> builder)
    {
        builder.Property(w => w.Balance).HasPrecision(18, 2);

        builder.HasOne(w => w.ApplicationUser)
            .WithOne(u => u.RewardWallet)
            .HasForeignKey<RewardWallet>(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
