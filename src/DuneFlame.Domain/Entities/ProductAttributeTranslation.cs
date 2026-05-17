using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of a ProductAttribute name in a specific language.
/// </summary>
public class ProductAttributeTranslation : BaseEntity
{
    public Guid ProductAttributeId { get; set; }
    public ProductAttribute? ProductAttribute { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// Translated attribute name (e.g., "اللون" for Color in Arabic)
    /// </summary>
    public string TranslatedName { get; set; } = string.Empty;
}
