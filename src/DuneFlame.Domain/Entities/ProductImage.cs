using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ProductImage : BaseEntity
{
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsMain { get; set; } = false; // Əsas şəkildirmi?

    /// <summary>
    /// SEO alt text for this image. Used in HTML alt attributes and Google Image indexing.
    /// If null, the frontend should fall back to the product name.
    /// </summary>
    public string? AltText { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
}