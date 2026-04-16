using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Brand : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Navigation Properties
    public ICollection<Product> Products { get; set; } = [];
}
