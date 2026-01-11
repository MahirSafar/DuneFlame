using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class RewardTransaction : BaseEntity
{
    public Guid WalletId { get; set; }
    public RewardWallet? RewardWallet { get; set; }
    public decimal Amount { get; set; }
    public RewardType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public Guid? RelatedOrderId { get; set; }
}
