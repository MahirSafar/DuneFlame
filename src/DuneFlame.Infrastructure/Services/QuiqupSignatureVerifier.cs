using System.Security.Cryptography;
using System.Text;
using DuneFlame.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Verifies inbound Quiqup webhook requests using the HMAC-SHA1 signature scheme
/// documented in the Quiqup Ecommerce API specification.
/// </summary>
/// <remarks>
/// Algorithm:
/// <list type="number">
///   <item><description>Quiqup sends an <c>X-Signature</c> header formatted as <c>sha1={hex-digest}</c>.</description></item>
///   <item><description>The hex-digest is <c>HMAC-SHA1(rawBody, WebhookSecret)</c>.</description></item>
///   <item><description>We recompute the same digest locally and compare with a timing-safe equality check.</description></item>
/// </list>
/// The <c>WebhookSecret</c> is configured via <c>Quiqup:WebhookSecret</c> in appsettings.
/// </remarks>
public sealed class QuiqupSignatureVerifier(
    IOptions<QuiqupSettings> options,
    ILogger<QuiqupSignatureVerifier> logger) : IQuiqupSignatureVerifier
{
    private const string SignaturePrefix = "sha1=";

    private readonly string _webhookSecret = options.Value.WebhookSecret;
    private readonly ILogger<QuiqupSignatureVerifier> _logger = logger;

    /// <inheritdoc />
    public bool IsValid(string rawBody, string? signatureHeader)
    {
        // 1. Guard: header must be present and correctly prefixed.
        if (string.IsNullOrEmpty(signatureHeader) ||
            !signatureHeader.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "[Quiqup Webhook] X-Signature header is missing or not prefixed with 'sha1='. " +
                "Received: '{Header}'", signatureHeader ?? "(null)");
            return false;
        }

        // 2. Guard: WebhookSecret must be configured.
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            _logger.LogError(
                "[Quiqup Webhook] Quiqup:WebhookSecret is not configured. " +
                "All webhook requests will be rejected until it is set.");
            return false;
        }

        // 3. Extract the hex portion after "sha1=".
        var receivedHex = signatureHeader[SignaturePrefix.Length..];

        // 4. Compute HMAC-SHA1(rawBody, WebhookSecret).
        var secretBytes = Encoding.UTF8.GetBytes(_webhookSecret);
        var bodyBytes   = Encoding.UTF8.GetBytes(rawBody);

        using var hmac = new HMACSHA1(secretBytes);
        var hashBytes    = hmac.ComputeHash(bodyBytes);
        var computedHex  = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // 5. Timing-safe comparison — prevents timing oracle attacks.
        //    FixedTimeEquals requires equal-length operands; the pre-check ensures a
        //    malformed (truncated or padded) header never throws ArgumentException.
        var computedBytes = Encoding.ASCII.GetBytes(computedHex);
        var receivedBytes = Encoding.ASCII.GetBytes(receivedHex.ToLowerInvariant());

        if (computedBytes.Length != receivedBytes.Length)
        {
            _logger.LogWarning(
                "[Quiqup Webhook] Signature length mismatch — possible truncated or malformed header. " +
                "Expected {Expected} hex chars, received {Received}.",
                computedBytes.Length, receivedBytes.Length);
            return false;
        }

        var isValid = CryptographicOperations.FixedTimeEquals(computedBytes, receivedBytes);

        if (!isValid)
        {
            _logger.LogWarning(
                "[Quiqup Webhook] HMAC-SHA1 signature mismatch. " +
                "Computed='{Computed}', Received='{Received}'",
                computedHex, receivedHex);
        }

        return isValid;
    }
}
