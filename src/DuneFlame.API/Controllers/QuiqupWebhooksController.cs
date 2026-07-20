using System.Text;
using System.Text.Json;
using DuneFlame.Domain.Enums;
using DuneFlame.Infrastructure.DTOs.Quiqup;
using DuneFlame.Infrastructure.Persistence;
using DuneFlame.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Receives and validates real-time order state update notifications from Quiqup.
/// POST api/v1/quiqup-webhooks
/// </summary>
/// <remarks>
/// Security model:
/// Every inbound request is validated against the <c>X-Signature</c> header (HMAC-SHA1)
/// before any payload processing occurs. Requests with a missing, malformed, or mismatched
/// signature are rejected with 401 Unauthorized.
///
/// The body must be read as a raw string BEFORE deserialisation to preserve the exact
/// byte sequence that Quiqup signed.
///
/// Sync model:
/// On a validated payload we locate the local <c>Order</c> by <c>QuiqupOrderId</c>,
/// map Quiqup's string state to our <see cref="DeliveryStatus"/> enum, and persist the
/// update. We always return 200 OK after validation — even if the order isn't found —
/// to prevent Quiqup from endlessly retrying the same event.
/// </remarks>
[Route("api/v1/quiqup-webhooks")]
[ApiController]
[AllowAnonymous]
public class QuiqupWebhooksController(
    IQuiqupSignatureVerifier signatureVerifier,
    ILogger<QuiqupWebhooksController> logger) : ControllerBase
{
    private const string SignatureHeaderName = "X-Signature";

    private readonly IQuiqupSignatureVerifier _signatureVerifier = signatureVerifier;
    private readonly ILogger<QuiqupWebhooksController> _logger = logger;

    // Deserialisation options — AllowReadingFromString covers Quiqup's inconsistent
    // numeric ID types across response schemas (some arrive as quoted strings).
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// Quiqup order webhook endpoint.
    /// POST /api/v1/quiqup-webhooks
    ///
    /// Lifecycle:
    /// 1. Buffer raw body string (HMAC input must be unmodified bytes).
    /// 2. Validate X-Signature header via HMAC-SHA1.
    /// 3. Deserialise into QuiqupWebhookRequest.
    /// 4. Look up local Order by QuiqupOrderId.
    /// 5. Map Quiqup state string → DeliveryStatus enum and persist.
    /// 6. Return 200 OK to acknowledge receipt.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HandleOrderWebhook(CancellationToken cancellationToken)
    {
        // ── Step 1: Buffer raw body BEFORE deserialisation ────────────────────
        string rawBody;
        using (var reader = new StreamReader(HttpContext.Request.Body, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }

        // ── Step 2: Validate X-Signature ──────────────────────────────────────
        var signatureHeader = Request.Headers[SignatureHeaderName].FirstOrDefault();

        if (!_signatureVerifier.IsValid(rawBody, signatureHeader))
        {
            _logger.LogWarning(
                "[Quiqup Webhook] Signature validation failed. " +
                "X-Signature='{Signature}', RemoteIp='{Ip}'",
                signatureHeader ?? "(missing)",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            return Unauthorized(new
            {
                error = "Invalid signature",
                message = "The X-Signature header does not match the expected HMAC-SHA1 digest."
            });
        }

        // ── Step 3: Deserialise ───────────────────────────────────────────────
        QuiqupWebhookRequest? webhookRequest;
        try
        {
            webhookRequest = JsonSerializer.Deserialize<QuiqupWebhookRequest>(rawBody, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "[Quiqup Webhook] Failed to deserialise body. First 500 chars: '{Body}'",
                rawBody.Length > 500 ? rawBody[..500] : rawBody);

            return BadRequest(new { error = "Invalid payload" });
        }

        if (webhookRequest is null)
        {
            _logger.LogWarning("[Quiqup Webhook] Deserialised payload was null.");
            return BadRequest(new { error = "Empty payload" });
        }

        // Guard: Quiqup may theoretically send "payload": null — treat as invalid.
        var quiqupPayload = webhookRequest.Payload;
        if (quiqupPayload is null)
        {
            _logger.LogWarning(
                "[Quiqup Webhook] Webhook 'payload' field was null. Action='{Action}', Type='{Type}'.",
                webhookRequest.Action, webhookRequest.Type);
            return BadRequest(new { error = "Null payload" });
        }

        _logger.LogInformation(
            "[Quiqup Webhook] Received: QuiqupOrderId={QuiqupOrderId}, PartnerRef={PartnerOrderId}, " +
            "Action='{Action}', Type='{Type}', State='{State}', SentAt={SentAt}.",
            quiqupPayload.Id,
            quiqupPayload.PartnerOrderId,
            webhookRequest.Action,
            webhookRequest.Type,
            quiqupPayload.State,
            webhookRequest.SentAt);

        // ── Step 4 & 5: Locate local Order and sync DeliveryStatus ───────────
        // Resolve AppDbContext from DI scope — the controller is [AllowAnonymous] so we
        // use RequestServices to keep the constructor lightweight (no IOrderService coupling).
        await SyncDeliveryStatusAsync(quiqupPayload, HttpContext.RequestServices, cancellationToken);

        // ── Step 6: Acknowledge receipt ───────────────────────────────────────
        // Always 200 OK after signature passes — Quiqup retries on non-2xx indefinitely.
        return Ok(new { received = true });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Finds the local Order by <c>QuiqupOrderId</c> and updates its
    /// <see cref="DeliveryStatus"/> to reflect the state reported in the webhook payload.
    /// </summary>
    private async Task SyncDeliveryStatusAsync(
        QuiqupOrderResponse quiqupPayload,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<AppDbContext>();

        // Look up the local order using Quiqup's numeric order ID.
        var order = await dbContext.Orders
            .FirstOrDefaultAsync(o => o.QuiqupOrderId == quiqupPayload.IdLongValue, cancellationToken);

        if (order is null)
        {
            // The order may not yet have QuiqupOrderId persisted (race condition between
            // Phase 4 SaveChanges and Quiqup's first webhook fire). Log as warning — not
            // an error — because re-delivery will resolve it once Phase 4 completes.
            _logger.LogWarning(
                "[Quiqup Webhook] No local Order found for QuiqupOrderId={QuiqupOrderId} " +
                "(PartnerRef={PartnerOrderId}). The webhook may have arrived before Phase 4 " +
                "persisted the Quiqup ID. Returning 200 to prevent re-delivery loops.",
                quiqupPayload.Id,
                quiqupPayload.PartnerOrderId);
            return;
        }

        var previousStatus = order.DeliveryStatus;
        var newStatus = MapQuiqupState(quiqupPayload.State);

        if (order.DeliveryStatus == newStatus)
        {
            _logger.LogInformation(
                "[Quiqup Webhook] Order {OrderId} (QuiqupOrderId={QuiqupOrderId}) is already " +
                "in DeliveryStatus={Status}. Idempotent update — no change persisted.",
                order.Id, quiqupPayload.Id, newStatus);
            return;
        }

        order.DeliveryStatus = newStatus;

        // If Quiqup delivers an updated tracking URL, refresh it.
        if (!string.IsNullOrWhiteSpace(quiqupPayload.TrackingUrl))
            order.QuiqupTrackingUrl = quiqupPayload.TrackingUrl;

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Quiqup Webhook] Order {OrderId} DeliveryStatus updated: {From} → {To} " +
            "(QuiqupState='{QuiqupState}', QuiqupOrderId={QuiqupOrderId}).",
            order.Id, previousStatus, newStatus, quiqupPayload.State, quiqupPayload.Id);
    }

    /// <summary>
    /// Maps a Quiqup order state string to our internal <see cref="DeliveryStatus"/> enum.
    /// Unknown states default to <see cref="DeliveryStatus.Failed"/> so that they are visible
    /// in admin dashboards rather than silently ignored.
    /// </summary>
    private static DeliveryStatus MapQuiqupState(string? quiqupState) =>
        quiqupState?.ToLowerInvariant() switch
        {
            "pending"               => DeliveryStatus.Pending,
            "ready_for_collection"  => DeliveryStatus.ReadyForCollection,
            "picked_up"             => DeliveryStatus.PickedUp,
            "in_transit"            => DeliveryStatus.InTransit,
            "delivered"             => DeliveryStatus.Delivered,
            "returned"              => DeliveryStatus.Returned,
            "cancelled"             => DeliveryStatus.Cancelled,
            _                       => DeliveryStatus.Failed   // includes "failed" + unknowns
        };
}
