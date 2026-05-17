using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of a ProductAttributeValue in a specific language.
/// </summary>
public class ProductAttributeValueTranslation : BaseEntity
{
    public Guid ProductAttributeValueId { get; set; }
    public ProductAttributeValue? ProductAttributeValue { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// Translated attribute value (e.g., "أحمر" for Red in Arabic)
    /// </summary>
    public string TranslatedValue { get; set; } = string.Empty;
}
