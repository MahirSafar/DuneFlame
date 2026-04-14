using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductVariantOption : BaseEntity
{
    public Guid ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public Guid ProductAttributeValueId { get; set; }
    public ProductAttributeValue? ProductAttributeValue { get; set; }
}
