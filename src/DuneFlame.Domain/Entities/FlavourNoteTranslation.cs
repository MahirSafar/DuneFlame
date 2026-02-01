using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a translation of a FlavourNote in a specific language.
/// Supports multi-language flavour note names without changing the base FlavourNote entity structure.
/// </summary>
public class FlavourNoteTranslation : BaseEntity
{
    public Guid FlavourNoteId { get; set; }
    public FlavourNote? FlavourNote { get; set; }
    
    /// <summary>
    /// Language code (e.g., "en", "ar")
    /// </summary>
    public string LanguageCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Translated flavour note name
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
