using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductImage : BaseEntity
{
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsMain { get; set; } = false; // Əsas şəkildirmi?

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
}