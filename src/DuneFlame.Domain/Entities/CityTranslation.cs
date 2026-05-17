using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Stores a localized name for a City in a specific language.
/// </summary>
public class CityTranslation : BaseEntity
{
    public Guid CityId { get; set; }
    public City? City { get; set; }

    /// <summary>Language code, e.g. "en" or "ar".</summary>
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>Translated city name, e.g. "دبي" for Dubai in Arabic.</summary>
    public string TranslatedName { get; set; } = string.Empty;
}
