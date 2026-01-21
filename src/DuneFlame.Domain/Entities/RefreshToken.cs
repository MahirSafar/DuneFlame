using DuneFlame.Domain.Common;
using System.ComponentModel.DataAnnotations;

namespace DuneFlame.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
    public bool IsExpired => DateTime.UtcNow >= Expires;

    public DateTime? Revoked { get; set; }
    public string? RevokedByIp { get; set; }
    public bool IsActive => Revoked == null && !IsExpired;

    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }

    // Optimistic concurrency token for detecting concurrent modifications
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
