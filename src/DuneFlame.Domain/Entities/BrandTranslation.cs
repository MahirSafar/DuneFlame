using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of a Brand in a specific language.
/// </summary>
public class BrandTranslation : BaseEntity
{
    public Guid BrandId { get; set; }
    public Brand? Brand { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// Translated brand name
    /// </summary>
    public string TranslatedName { get; set; } = string.Empty;

    /// <summary>
    /// Translated brand description (optional)
    /// </summary>
    public string? TranslatedDescription { get; set; }
}
