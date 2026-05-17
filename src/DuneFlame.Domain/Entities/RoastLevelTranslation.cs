using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of a RoastLevelEntity name in a specific language.
/// </summary>
public class RoastLevelTranslation : BaseEntity
{
    public Guid RoastLevelId { get; set; }
    public RoastLevelEntity? RoastLevel { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// Translated roast level name (e.g., "تحميص خفيف" for Light Roast in Arabic)
    /// </summary>
    public string TranslatedName { get; set; } = string.Empty;
}
