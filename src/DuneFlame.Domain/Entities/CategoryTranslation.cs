using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of a Category in a specific language.
/// Supports multi-language content without changing the base Category entity structure.
/// </summary>
public class CategoryTranslation : BaseEntity
{
    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }
    
    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Translated category name
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
