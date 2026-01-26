using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class CartItem : BaseEntity
{
    public Guid CartId { get; set; }
    public Cart? Cart { get; set; }
    public Guid ProductPriceId { get; set; }
    public ProductPrice? ProductPrice { get; set; }
    public Guid RoastLevelId { get; set; }
    public RoastLevelEntity? RoastLevel { get; set; }
    public Guid GrindTypeId { get; set; }
    public GrindType? GrindType { get; set; }
    public int Quantity { get; set; }
}
