using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of a GrindType name in a specific language.
/// </summary>
public class GrindTypeTranslation : BaseEntity
{
    public Guid GrindTypeId { get; set; }
    public GrindType? GrindType { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// Translated grind type name (e.g., "حبة كاملة" for Whole Bean in Arabic)
    /// </summary>
    public string TranslatedName { get; set; } = string.Empty;
}
