using DuneFlame.Domain.Common;

namespace DuneFlame.Domain.Entities;

public class NewsletterSubscription : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public bool IsVerified { get; set; } = false; // Email təsdiqlənibmi?

    // Tokens
    public string? VerificationToken { get; set; }
    public string? UnsubscribeToken { get; set; }

    public string? Source { get; set; } // Məs: "Footer", "Popup", "Checkout"
}