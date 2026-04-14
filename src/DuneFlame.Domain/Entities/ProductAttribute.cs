using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductAttribute : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public ICollection<ProductAttributeValue> Values { get; set; } = [];
}
