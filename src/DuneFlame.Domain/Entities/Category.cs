using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Category : BaseEntity
{
    public string Slug { get; set; } = string.Empty; // URL üçün (meselen: "qehve-deneleri")

    // Relationships
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<CategoryTranslation> Translations { get; set; } = new List<CategoryTranslation>();
}