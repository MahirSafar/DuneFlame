namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Contract for verifying that an inbound Quiqup webhook request has not been
/// tampered with, using the HMAC-SHA1 signature scheme documented by Quiqup.
/// </summary>
public interface IQuiqupSignatureVerifier
{
    /// <summary>
    /// Verifies that the HMAC-SHA1 signature in the <c>X-Signature</c> header matches
    /// a locally computed digest of the raw request body using the configured
    /// <c>Quiqup:WebhookSecret</c>.
    /// </summary>
    /// <param name="rawBody">
    /// The raw UTF-8 request body string read from <c>HttpContext.Request.Body</c>
    /// <b>before</b> any JSON deserialisation. Quiqup signs the exact bytes it sends,
    /// so this must be the unmodified body.
    /// </param>
    /// <param name="signatureHeader">
    /// The full value of the <c>X-Signature</c> header as received from Quiqup,
    /// e.g. <c>sha1=148a6d4a0e95dada696d20f702caf027b548704a</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the computed digest matches the received signature;
    /// <see langword="false"/> if the header is missing, malformed, or the digests differ.
    /// </returns>
    bool IsValid(string rawBody, string? signatureHeader);
}
