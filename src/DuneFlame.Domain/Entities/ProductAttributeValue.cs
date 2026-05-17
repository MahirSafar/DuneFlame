using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductAttributeValue : BaseEntity
{
    public Guid ProductAttributeId { get; set; }
    public ProductAttribute? ProductAttribute { get; set; }

    public string Value { get; set; } = string.Empty;
    public ICollection<ProductAttributeValueTranslation> Translations { get; set; } = new List<ProductAttributeValueTranslation>();
}
