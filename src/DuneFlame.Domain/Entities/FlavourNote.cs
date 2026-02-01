using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

/// <summary>
/// Represents a flavor note associated with a product.
/// Multiple flavor notes can be associated with a single product.
/// </summary>
public class FlavourNote : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>
    /// The flavor note name/description (e.g., "Chocolate", "Fruity", "Nutty")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Display order of the flavor note
    /// </summary>
    public int DisplayOrder { get; set; } = 0;

    /// <summary>
    /// Navigation property for translations
    /// </summary>
    public ICollection<FlavourNoteTranslation> Translations { get; set; } = [];
}
