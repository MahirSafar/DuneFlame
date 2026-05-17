using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of an Origin name in a specific language.
/// </summary>
public class OriginTranslation : BaseEntity
{
    public Guid OriginId { get; set; }
    public Origin? Origin { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// Translated origin name (e.g., "إثيوبيا" for Ethiopia in Arabic)
    /// </summary>
    public string TranslatedName { get; set; } = string.Empty;
}
