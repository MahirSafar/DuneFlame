using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class PasswordResetToken : BaseEntity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    
    public DateTime? UsedAt { get; set; }
    public bool IsUsed => UsedAt.HasValue;
}
