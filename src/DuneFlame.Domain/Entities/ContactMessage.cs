using DuneFlame.Domain.Common;
using DuneFlame.Domain.Enums;

namespace DuneFlame.Domain.Entities;

public class ContactMessage : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Subject { get; set; }
    public InquiryType InquiryType { get; set; }
    public string Message { get; set; } = string.Empty;

    public string? IpAddress { get; set; } // Təhlükəsizlik üçün
    public bool IsRead { get; set; } = false; // Admin oxuyubmu?
    public string? AdminReply { get; set; } // Cavab yazılıbmı?
}