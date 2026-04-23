using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Category : BaseEntity
{
    public string Slug { get; set; } = string.Empty;
    public bool IsCoffeeCategory { get; set; } = false;

    // Hierarchy — NON-NULLABLE. Root category uses Guid.Empty as sentinel.
    // All other categories point to a real parent Category.Id.
    public Guid ParentCategoryId { get; set; } = Guid.Empty;

    // Navigation
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = [];

    // Relationships
    public ICollection<Product> Products { get; set; } = [];
    public ICollection<CategoryTranslation> Translations { get; set; } = [];
}