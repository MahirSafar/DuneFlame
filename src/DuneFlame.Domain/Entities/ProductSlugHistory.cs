namespace DuneFlame.Domain.Entities;

/// <summary>
/// Records every slug a product has ever used.
/// When a product's slug changes, the old slug is stored here so that
/// requests to the old URL can be 301-redirected to the current slug,
/// preserving SEO link equity.
/// </summary>
public class ProductSlugHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>
    /// The old slug that is no longer active.
    /// </summary>
    public string OldSlug { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of when the slug was retired (i.e., when the product was updated).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
