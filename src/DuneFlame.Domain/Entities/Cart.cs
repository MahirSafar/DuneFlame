using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Cart : BaseEntity
{
    public Guid UserId { get; set; }
    public ApplicationUser? ApplicationUser { get; set; }
    public ICollection<CartItem> Items { get; set; } = [];
}
