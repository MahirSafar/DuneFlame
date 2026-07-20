using DuneFlame.Application.Interfaces;
using DuneFlame.Domain.Enums;
using DuneFlame.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuneFlame.API.Controllers;

/// <summary>
/// Admin-only endpoints that expose the Quiqup last-mile courier lifecycle actions
/// for a specific order. All routes require the internal <b>Order GUID</b> as
/// <c>{id}</c>, which is looked up in the local database before any Quiqup API call.
///
/// Route prefix: <c>api/v1/admin/orders/{id:guid}/delivery</c>
/// </summary>
/// <remarks>
/// Design contract:
/// <list type="bullet">
///   <item>Every endpoint resolves the local Order first and validates that
///   <see cref="Domain.Entities.Order.QuiqupOrderId"/> is populated before
///   calling any Quiqup API method.</item>
///   <item>The <c>/retry-submit</c> endpoint is the only one that does NOT require
///   <c>QuiqupOrderId</c> — it is specifically the recovery path for orders whose
///   Phase 4 submission failed.</item>
///   <item>All Quiqup-state transitions are reflected back into
///   <see cref="DeliveryStatus"/> and persisted immediately after each call.</item>
///   <item>Every action captures the ASP.NET Core request <see cref="CancellationToken"/>
///   and propagates it into both the Quiqup service layer and EF Core
///   <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>
///   calls so that client disconnects abort in-flight work cleanly.</item>
/// </list>
/// </remarks>
[Route("api/v1/admin/orders/{id:guid}/delivery")]
[ApiController]
[Authorize(Roles = "Admin")]
public class AdminDeliveryController(
    AppDbContext dbContext,
    IQuiqupDeliveryService quiqupDeliveryService,
    ILogger<AdminDeliveryController> logger) : ControllerBase
{
    private readonly AppDbContext _db = dbContext;
    private readonly IQuiqupDeliveryService _quiqupDeliveryService = quiqupDeliveryService;
    private readonly ILogger<AdminDeliveryController> _logger = logger;

    // ── POST /mark-ready ──────────────────────────────────────────────────────

    /// <summary>
    /// Transitions the order from <c>pending</c> to <c>ready_for_collection</c>
    /// on the Quiqup platform, notifying couriers to pick up the parcel.
    ///
    /// POST api/v1/admin/orders/{id}/delivery/mark-ready
    /// </summary>
    /// <param name="id">Internal DuneFlame Order GUID.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifetime.</param>
    [HttpPost("mark-ready")]
    public async Task<IActionResult> MarkReadyForCollection(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders.FindAsync([id], cancellationToken);

        if (order is null)
            return NotFound(new { message = $"Order {id} not found." });

        if (order.QuiqupOrderId is null)
            return BadRequest(new
            {
                message = "Order does not have a Quiqup order ID. " +
                          "The order may not have been submitted to Quiqup yet. " +
                          "Use POST /retry-submit to trigger the initial submission."
            });

        try
        {
            var result = await _quiqupDeliveryService.MarkReadyForCollectionAsync(
                order.QuiqupOrderId.Value,
                cancellationToken);

            order.DeliveryStatus = DeliveryStatus.ReadyForCollection;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[AdminDelivery] Order {OrderId} (QuiqupOrderId={QuiqupOrderId}) " +
                "marked ready_for_collection. State='{State}'.",
                id, order.QuiqupOrderId, result.State);

            return Ok(new
            {
                message     = "Order marked as ready for collection.",
                state       = result.State,
                trackingUrl = result.TrackingUrl
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "[AdminDelivery] Database update failed after mark-ready for Order {OrderId}.", id);
            return StatusCode(500, new
            {
                message = "Quiqup state was updated but the local database write failed. " +
                          "Please use GET /sync to reconcile.",
                detail  = ex.InnerException?.Message ?? ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AdminDelivery] Failed to mark order {OrderId} ready for collection.", id);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ── GET /label ────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the official Air Waybill (AWB) PDF label from Quiqup for printing.
    ///
    /// GET api/v1/admin/orders/{id}/delivery/label
    /// </summary>
    /// <param name="id">Internal DuneFlame Order GUID.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifetime.</param>
    /// <returns>
    /// <c>application/pdf</c> file stream with the AWB label, named
    /// <c>awb_label_{id}.pdf</c>.
    /// </returns>
    [HttpGet("label")]
    public async Task<IActionResult> DownloadLabel(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders.FindAsync([id], cancellationToken);

        if (order is null)
            return NotFound(new { message = $"Order {id} not found." });

        if (order.QuiqupOrderId is null)
            return BadRequest(new
            {
                message = "Cannot download label — order has no Quiqup order ID."
            });

        try
        {
            var pdfBytes = await _quiqupDeliveryService.DownloadOrderLabelAsync(
                order.QuiqupOrderId.Value,
                cancellationToken);

            _logger.LogInformation(
                "[AdminDelivery] AWB label downloaded for Order {OrderId} " +
                "(QuiqupOrderId={QuiqupOrderId}). Size={Bytes} bytes.",
                id, order.QuiqupOrderId, pdfBytes.Length);

            return File(pdfBytes, "application/pdf", $"awb_label_{id}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AdminDelivery] Failed to download AWB label for order {OrderId}.", id);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ── GET /sync ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the live delivery state from Quiqup and updates the local
    /// <see cref="DeliveryStatus"/> to reflect real-time courier progress.
    ///
    /// GET api/v1/admin/orders/{id}/delivery/sync
    /// </summary>
    /// <param name="id">Internal DuneFlame Order GUID.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifetime.</param>
    [HttpGet("sync")]
    public async Task<IActionResult> SyncDeliveryState(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders.FindAsync([id], cancellationToken);

        if (order is null)
            return NotFound(new { message = $"Order {id} not found." });

        if (order.QuiqupOrderId is null)
            return BadRequest(new
            {
                message = "Cannot sync — order has no Quiqup order ID."
            });

        try
        {
            var result = await _quiqupDeliveryService.GetOrderByIdAsync(
                order.QuiqupOrderId.Value,
                cancellationToken);

            var previousStatus = order.DeliveryStatus;
            order.DeliveryStatus = MapQuiqupState(result.State);

            if (!string.IsNullOrWhiteSpace(result.TrackingUrl))
                order.QuiqupTrackingUrl = result.TrackingUrl;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[AdminDelivery] Order {OrderId} synced. DeliveryStatus: {From} → {To} " +
                "(QuiqupState='{State}').",
                id, previousStatus, order.DeliveryStatus, result.State);

            return Ok(new
            {
                orderId        = id,
                quiqupOrderId  = result.QuiqupOrderId,
                quiqupState    = result.State,
                deliveryStatus = order.DeliveryStatus.ToString(),
                trackingUrl    = result.TrackingUrl
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "[AdminDelivery] Database update failed after sync for Order {OrderId}.", id);
            return StatusCode(500, new
            {
                message = "Quiqup state was fetched but the local database write failed.",
                detail  = ex.InnerException?.Message ?? ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AdminDelivery] Failed to sync delivery state for order {OrderId}.", id);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ── POST /cancel-quiqup ───────────────────────────────────────────────────

    /// <summary>
    /// Cancels the shipment task on the Quiqup platform and reflects the
    /// cancellation in the local <see cref="DeliveryStatus"/>.
    ///
    /// POST api/v1/admin/orders/{id}/delivery/cancel-quiqup
    ///
    /// Note: This only cancels the <b>Quiqup courier task</b>. It does NOT cancel
    /// the DuneFlame order or trigger a Stripe refund. Use
    /// <c>POST /api/v1/admin/orders/{id}/cancel</c> for the full order cancellation.
    /// </summary>
    /// <param name="id">Internal DuneFlame Order GUID.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifetime.</param>
    [HttpPost("cancel-quiqup")]
    public async Task<IActionResult> CancelQuiqupShipment(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders.FindAsync([id], cancellationToken);

        if (order is null)
            return NotFound(new { message = $"Order {id} not found." });

        if (order.QuiqupOrderId is null)
            return BadRequest(new
            {
                message = "Cannot cancel on Quiqup — order has no Quiqup order ID."
            });

        try
        {
            var result = await _quiqupDeliveryService.CancelOrderAsync(
                order.QuiqupOrderId.Value,
                cancellationToken);

            order.DeliveryStatus = DeliveryStatus.Cancelled;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[AdminDelivery] Quiqup shipment cancelled for Order {OrderId} " +
                "(QuiqupOrderId={QuiqupOrderId}). QuiqupState='{State}'.",
                id, order.QuiqupOrderId, result.State);

            return Ok(new
            {
                message        = "Quiqup shipment task cancelled.",
                quiqupState    = result.State,
                deliveryStatus = DeliveryStatus.Cancelled.ToString()
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "[AdminDelivery] Database update failed after Quiqup cancellation for Order {OrderId}.", id);
            return StatusCode(500, new
            {
                message = "Quiqup order was cancelled but the local database write failed.",
                detail  = ex.InnerException?.Message ?? ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AdminDelivery] Failed to cancel Quiqup shipment for order {OrderId}.", id);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ── POST /retry-submit ────────────────────────────────────────────────────

    /// <summary>
    /// Recovery endpoint for orders whose Phase 4 Quiqup submission failed
    /// (i.e., <see cref="Domain.Entities.Order.QuiqupOrderId"/> is null).
    ///
    /// POST api/v1/admin/orders/{id}/delivery/retry-submit
    ///
    /// Re-runs <see cref="IQuiqupDeliveryService.CreateOrderAsync"/> with the
    /// full domain Order (including Items) and persists the Quiqup identifiers
    /// on success, exactly mirroring Phase 4 in <c>ProcessPaymentSuccessAsync</c>.
    /// </summary>
    /// <param name="id">Internal DuneFlame Order GUID.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifetime.</param>
    [HttpPost("retry-submit")]
    public async Task<IActionResult> RetryQuiqupSubmission(
        Guid id,
        CancellationToken cancellationToken)
    {
        // Eagerly load Items → ProductVariant → Product → Category so that
        // BuildOrderPayload can resolve per-category fallback weights via GetFallbackWeightKg.
        // Note: null-forgiving (!) on nullable nav properties is intentional — EF Core
        // handles null navigation gracefully at runtime; the ! suppresses CS8602 warnings.
        var order = await _db.Orders
            .Include(o => o.ApplicationUser)           // customer name & phone for Quiqup destination
            .Include(o => o.Items)
                .ThenInclude(i => i.ProductVariant!)
                .ThenInclude(pv => pv.Product!)
                .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
            return NotFound(new { message = $"Order {id} not found." });

        if (order.QuiqupOrderId is not null)
            return BadRequest(new
            {
                message = $"Order {id} already has QuiqupOrderId={order.QuiqupOrderId}. " +
                          "Use GET /sync to refresh the live state instead."
            });

        try
        {
            var result = await _quiqupDeliveryService.CreateOrderAsync(order, cancellationToken);

            order.QuiqupOrderId     = result.QuiqupOrderId;
            order.QuiqupTrackingUrl = result.TrackingUrl;
            order.DeliveryStatus    = DeliveryStatus.ReadyForCollection;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[AdminDelivery] Retry-submit succeeded for Order {OrderId}. " +
                "QuiqupOrderId={QuiqupOrderId}, TrackingUrl={TrackingUrl}.",
                id, result.QuiqupOrderId, result.TrackingUrl);

            return Ok(new
            {
                message        = "Order successfully submitted to Quiqup.",
                quiqupOrderId  = result.QuiqupOrderId,
                trackingUrl    = result.TrackingUrl,
                deliveryStatus = DeliveryStatus.ReadyForCollection.ToString()
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "[AdminDelivery] Quiqup order created but database persist failed for Order {OrderId}. " +
                "QuiqupOrderId may have been assigned — check Quiqup portal.", id);
            return StatusCode(500, new
            {
                message = "Quiqup order was created but the local database write failed. " +
                          "Check the Quiqup portal and use GET /sync to reconcile.",
                detail  = ex.InnerException?.Message ?? ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[AdminDelivery] Retry-submit FAILED for Order {OrderId}.", id);
            return StatusCode(500, new { message = ex.Message });
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Maps a Quiqup order state string to our internal <see cref="DeliveryStatus"/>.
    /// Mirrors the identical mapping in <see cref="QuiqupWebhooksController"/>.
    /// Unknown states default to <see cref="DeliveryStatus.Failed"/> so they are
    /// surfaced visually in admin dashboards rather than silently ignored.
    /// </summary>
    private static DeliveryStatus MapQuiqupState(string? state) =>
        state?.ToLowerInvariant() switch
        {
            "pending"              => DeliveryStatus.Pending,
            "ready_for_collection" => DeliveryStatus.ReadyForCollection,
            "picked_up"            => DeliveryStatus.PickedUp,
            "in_transit"           => DeliveryStatus.InTransit,
            "delivered"            => DeliveryStatus.Delivered,
            "returned"             => DeliveryStatus.Returned,
            "cancelled"            => DeliveryStatus.Cancelled,
            _                      => DeliveryStatus.Failed
        };
}
