using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;
using System;

namespace DuneFlame.Domain.Entities;

public class ProductVariantPrice : BaseEntity
{
    public Guid ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public Currency Currency { get; set; }
    public decimal Price { get; set; }
}
