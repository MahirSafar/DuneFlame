using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductEquipmentProfile : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public Dictionary<string, string> Specifications { get; set; } = new();
}
