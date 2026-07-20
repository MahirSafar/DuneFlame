using DuneFlame.Application.DTOs.Delivery;
using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

/// <summary>
/// Abstraction for the Quiqup Last-Mile Delivery integration.
/// Implementations live in the Infrastructure layer and are registered as typed HttpClients.
/// </summary>
public interface IQuiqupDeliveryService
{
    /// <summary>
    /// Returns a valid OAuth2 Bearer token for the Quiqup API.
    /// Tokens are cached in IMemoryCache until <c>expires_in - 60s</c> to absorb network latency.
    /// A fresh token is fetched automatically when the cached one has expired.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The raw access-token string ready to be passed as a Bearer header.</returns>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new delivery order on the Quiqup Ecommerce API (POST /orders).
    /// The order is dispatched as <c>pre_paid</c> (payment already collected via Stripe)
    /// with <c>partner_next_day</c> delivery and will be created in <c>pending</c> state.
    /// </summary>
    /// <param name="domainOrder">
    /// The fully populated domain <see cref="Order"/> entity, including
    /// <see cref="Order.Items"/>, <see cref="Order.ApplicationUser"/>, and
    /// <see cref="Order.ShippingAddress"/>.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A <see cref="QuiqupDeliveryResult"/> containing the Quiqup order ID, UUID,
    /// and tracking URLs to persist in our database.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when Quiqup returns a non-success HTTP status code.
    /// The structured <c>api_error</c> details are logged before the exception is raised.
    /// </exception>
    Task<QuiqupDeliveryResult> CreateOrderAsync(Order domainOrder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a Quiqup order from <c>pending</c> to <c>ready_for_collection</c> state
    /// via PUT /orders/{order_id}/ready_for_collection, making it visible to Quiqup
    /// couriers and scheduling it for the next collection run.
    /// </summary>
    /// <param name="quiqupOrderId">
    /// Quiqup's internal numeric order ID as returned in <see cref="QuiqupDeliveryResult.QuiqupOrderId"/>
    /// from a prior <see cref="CreateOrderAsync"/> call. This is the path parameter sent to Quiqup.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// An updated <see cref="QuiqupDeliveryResult"/> reflecting the new <c>ready_for_collection</c>
    /// state, with refreshed tracking URLs and timestamps.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when Quiqup returns a non-success HTTP status code, for example:
    /// <list type="bullet">
    ///   <item><description>404 — the <paramref name="quiqupOrderId"/> does not exist.</description></item>
    ///   <item><description>422 — the order is in a state that cannot be activated (e.g., already cancelled).</description></item>
    /// </list>
    /// The structured <c>api_error</c> details are logged before the exception is raised.
    /// </exception>
    Task<QuiqupDeliveryResult> MarkReadyForCollectionAsync(
        long quiqupOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a paginated, filtered list of delivery orders from the Quiqup Ecommerce API
    /// (GET /orders).
    /// </summary>
    /// <param name="fromDate">
    /// Inclusive start of the date range to filter by (sent as <c>from=yyyy-MM-dd</c>).
    /// </param>
    /// <param name="toDate">
    /// Inclusive end of the date range to filter by (sent as <c>to=yyyy-MM-dd</c>).
    /// </param>
    /// <param name="stateFilter">
    /// Order lifecycle state to filter by (sent as <c>filters[state]=...</c>).
    /// Use the <see cref="QuiqupOrderState"/> constants to avoid magic strings,
    /// e.g. <c>QuiqupOrderState.Pending</c> or <c>QuiqupOrderState.DeliveryComplete</c>.
    /// </param>
    /// <param name="page">1-indexed page number. Defaults to 1.</param>
    /// <param name="perPage">Maximum number of orders per page. Defaults to 20.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A read-only list of <see cref="QuiqupDeliveryResult"/> records for the matching orders.
    /// Returns an empty list when no orders match the filter criteria.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when Quiqup returns a non-success HTTP status code.
    /// The structured <c>api_error</c> details are logged before the exception is raised.
    /// </exception>
    Task<IReadOnlyList<QuiqupDeliveryResult>> ListOrdersAsync(
        DateTime fromDate,
        DateTime toDate,
        string stateFilter,
        int page = 1,
        int perPage = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the official Air Waybill (AWB) label for a Quiqup order
    /// via GET /order_label/{order_id}.
    /// The label must be printed and physically attached to the parcel before
    /// handing it to Quiqup couriers for collection.
    /// </summary>
    /// <param name="quiqupOrderId">
    /// Quiqup's internal numeric order ID as returned in <see cref="QuiqupDeliveryResult.QuiqupOrderId"/>.
    /// This is embedded directly in the URL path.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// The raw PDF binary payload (<c>byte[]</c>) ready to be saved to disk, stored in blob storage,
    /// or streamed to the browser as <c>Content-Type: application/pdf</c>.
    /// </returns>
    /// <remarks>
    /// Unlike all other Quiqup endpoints, this one returns <c>application/pdf</c> on success,
    /// not a JSON envelope. Error responses (non-2xx) still use the standard <c>api_error</c> JSON
    /// structure and are logged accordingly before an exception is raised.
    /// </remarks>
    /// <exception cref="HttpRequestException">
    /// Thrown when Quiqup returns a non-success HTTP status code.
    /// The structured <c>api_error</c> details are logged before the exception is raised where available.
    /// </exception>
    Task<byte[]> DownloadOrderLabelAsync(
        long quiqupOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current real-time state and full details of a single Quiqup delivery order
    /// via GET /orders/{order_id}. Use this to poll status between webhook notifications or to
    /// fetch delivery attempt counts, failure reasons, and tracking URLs on demand.
    /// </summary>
    /// <param name="quiqupOrderId">
    /// Quiqup's internal numeric order ID as returned in <see cref="QuiqupDeliveryResult.QuiqupOrderId"/>.
    /// Embedded directly in the URL path.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// A <see cref="QuiqupDeliveryResult"/> reflecting the order's current lifecycle state,
    /// tracking URL, and partner order ID.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when Quiqup returns a non-success HTTP status code (e.g., 404 if the order
    /// does not exist). The structured <c>api_error</c> details are logged before throwing.
    /// </exception>
    Task<QuiqupDeliveryResult> GetOrderByIdAsync(
        long quiqupOrderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the payment details of a Quiqup order that is still in the
    /// <c>pending</c> state via PUT /orders/{order_id}.
    /// Only <c>payment_mode</c> and <c>payment_amount</c> can be changed;
    /// both must always be provided together.
    /// </summary>
    /// <param name="quiqupOrderId">
    /// Quiqup's internal numeric order ID. Embedded directly in the URL path.
    /// </param>
    /// <param name="paymentMode">
    /// The new payment mode to apply. Use <see cref="QuiqupPaymentMode"/> constants
    /// (e.g., <c>QuiqupPaymentMode.CashOnDelivery</c>) to avoid magic strings.
    /// </param>
    /// <param name="paymentAmount">
    /// The payment amount. Must be exactly <c>0.0</c> when <paramref name="paymentMode"/>
    /// is <c>pre_paid</c>; must be a positive number for all COD/POD modes.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A completed <see cref="Task"/> on success (Quiqup returns an empty <c>{}</c> body).</returns>
    /// <exception cref="ArgumentException">
    /// Thrown immediately (before any network call) when the
    /// <paramref name="paymentMode"/>/<paramref name="paymentAmount"/> combination
    /// violates Quiqup's documented business rule.
    /// </exception>
    /// <exception cref="HttpRequestException">
    /// Thrown when Quiqup returns a non-success HTTP status code (e.g., 422 if the
    /// order is no longer in <c>pending</c> state). Structured <c>api_error</c> details
    /// are logged before throwing.
    /// </exception>
    Task UpdatePendingOrderPaymentAsync(
        long quiqupOrderId,
        string paymentMode,
        double paymentAmount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an additional physical parcel to a shipment task via
    /// POST /orders/{order_id}/parcels. Can only be performed on orders
    /// that are still in the <c>pending</c> state.
    /// </summary>
    /// <param name="quiqupOrderId">
    /// Quiqup's internal numeric order ID. Embedded directly in the URL path.
    /// </param>
    /// <param name="parcelName">
    /// Parcel reference name or product tag visible on the Quiqup portal and to couriers
    /// (e.g., <c>"Espresso Blend 500g – Order #1042 Parcel 2 of 3"</c>).
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A completed <see cref="Task"/> on success (Quiqup returns an empty <c>{}</c> body).</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when Quiqup returns a non-success HTTP status code (e.g., 422 if the
    /// order is no longer in <c>pending</c> state). Structured <c>api_error</c> details
    /// are logged before throwing.
    /// </exception>
    Task AddParcelToPendingOrderAsync(
        long quiqupOrderId,
        string parcelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a specific parcel from a shipment task via
    /// DELETE /orders/{order_id}/parcels/{parcel_id}.
    /// Can only be performed on orders that are still in the <c>pending</c> state.
    /// </summary>
    /// <param name="quiqupOrderId">
    /// Quiqup's internal numeric order ID. Embedded as the first path segment.
    /// </param>
    /// <param name="quiqupParcelId">
    /// Quiqup's parcel identifier string as returned in the items array
    /// (use <c>QuiqupResponseItem.IdValue</c> to obtain a normalised string regardless
    /// of whether Quiqup returned the ID as a number or a quoted string).
    /// Embedded as the second path segment.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>A completed <see cref="Task"/> on success (Quiqup returns an empty <c>{}</c> body).</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when Quiqup returns a non-success HTTP status code. Common error scenarios:
    /// <list type="bullet">
    ///   <item><description>422 — attempting to remove the last remaining parcel from an order (Quiqup enforces a minimum of one parcel).</description></item>
    ///   <item><description>422 — the order is no longer in <c>pending</c> state.</description></item>
    ///   <item><description>404 — the order or parcel does not exist.</description></item>
    /// </list>
    /// Structured <c>api_error</c> details are logged before throwing.
    /// </exception>
    Task RemoveParcelFromPendingOrderAsync(
        long quiqupOrderId,
        string quiqupParcelId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a <c>pending</c> Quiqup order via PUT /orders/batch/set_cancelled.
    /// Although the underlying API is a batch endpoint, this method wraps a single-order
    /// cancellation for the standard application workflow.
    /// </summary>
    /// <param name="quiqupOrderId">
    /// Quiqup's internal numeric order ID to cancel.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be cancelled.</param>
    /// <returns>
    /// An updated <see cref="QuiqupDeliveryResult"/> reflecting the <c>cancelled</c> state.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when Quiqup returns a non-success HTTP status code (e.g., 422 if the order
    /// is already past the <c>pending</c> state and cannot be cancelled). Structured
    /// <c>api_error</c> details are logged before throwing.
    /// </exception>
    Task<QuiqupDeliveryResult> CancelOrderAsync(
        long quiqupOrderId,
        CancellationToken cancellationToken = default);
}
