using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Category : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty; // URL üçün (meselen: "qehve-deneleri")

    // Relationship
    public ICollection<Product> Products { get; set; } = new List<Product>();
}