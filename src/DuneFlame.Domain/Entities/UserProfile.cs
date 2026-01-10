using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class UserProfile : BaseEntity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime? DateOfBirth { get; set; }
}
