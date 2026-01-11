namespace DuneFlame.Infrastructure.Authentication;

public class StripeSettings
{
    public const string SectionName = "StripeSettings";
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}
