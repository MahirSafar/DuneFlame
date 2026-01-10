using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class ExternalLogin : BaseEntity
{
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    
    public string LoginProvider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string ProviderDisplayName { get; set; } = string.Empty;
    
    public DateTime ConnectedAt { get; set; }
}
