using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Stores a localized name for a Country in a specific language.
/// </summary>
public class CountryTranslation : BaseEntity
{
    public Guid CountryId { get; set; }
    public Country? Country { get; set; }

    /// <summary>Language code, e.g. "en" or "ar".</summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>Translated country name, e.g. "الإمارات العربية المتحدة" for AE in Arabic.</summary>
    public string TranslatedName { get; set; } = string.Empty;
}
