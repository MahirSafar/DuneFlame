using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of an AboutSection in a specific language.
/// </summary>
public class AboutSectionTranslation : BaseEntity
{
    public Guid AboutSectionId { get; set; }
    public AboutSection? AboutSection { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// Translated section title
    /// </summary>
    public string TranslatedTitle { get; set; } = string.Empty;

    /// <summary>
    /// Translated section content
    /// </summary>
    public string TranslatedContent { get; set; } = string.Empty;
}
