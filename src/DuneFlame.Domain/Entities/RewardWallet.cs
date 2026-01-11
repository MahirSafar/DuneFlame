using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class RewardWallet : BaseEntity
{
    public Guid UserId { get; set; }
    public ApplicationUser? ApplicationUser { get; set; }
    public decimal Balance { get; set; } = 0;
    public ICollection<RewardTransaction> Transactions { get; set; } = [];
}
