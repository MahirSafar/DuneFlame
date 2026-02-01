using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of a Product in a specific language.
/// Supports multi-language content without changing the base Product entity structure.
/// </summary>
public class ProductTranslation : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    
    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Translated product name
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Translated product description
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
