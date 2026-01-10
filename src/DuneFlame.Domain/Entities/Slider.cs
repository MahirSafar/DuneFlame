using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class Slider : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty; // Klikləyəndə hara getsin?
    public int Order { get; set; } // Sıralama (1, 2, 3...)
    public bool IsActive { get; set; } = true;
}