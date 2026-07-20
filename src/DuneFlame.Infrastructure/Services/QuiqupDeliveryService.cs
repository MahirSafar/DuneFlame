using DuneFlame.Application.DTOs.Delivery;
using DuneFlame.Infrastructure.Configuration;
using DuneFlame.Infrastructure.DTOs.Quiqup;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DuneFlame.Infrastructure.Services;

/// <summary>
/// Infrastructure implementation of <see cref="IQuiqupDeliveryService"/>.
/// Manages OAuth2 client-credentials authentication with the Quiqup API and caches
/// the resulting access token to avoid redundant network round-trips.
/// </summary>
public class QuiqupDeliveryService : IQuiqupDeliveryService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly QuiqupSettings _settings;
    private readonly ILogger<QuiqupDeliveryService> _logger;

    /// <summary>Cache key used to store the Quiqup access token in IMemoryCache.</summary>
    private const string TokenCacheKey = "Quiqup_Auth_Token";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // AllowReadingFromString: Quiqup is inconsistent across endpoints — some responses
        // return nested IDs as quoted strings (e.g., cancel batch: "id": "1307392"),
        // while others return bare integers. This flag makes the deserializer accept both
        // forms transparently for all numeric properties without throwing JsonException.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    public QuiqupDeliveryService(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<QuiqupSettings> settings,
        ILogger<QuiqupDeliveryService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    // ── Token Acquisition ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Return the cached token while it is still valid.
        if (_cache.TryGetValue(TokenCacheKey, out string? cachedToken) && !string.IsNullOrEmpty(cachedToken))
        {
            return cachedToken;
        }

        _logger.LogInformation("[Quiqup Auth] Cached token missing or expired — requesting a new OAuth2 access token.");

        // Parameters are passed in the query string as required by the Quiqup technical specification.
        var requestUrl = $"/oauth/token?grant_type=client_credentials" +
                         $"&client_id={Uri.EscapeDataString(_settings.ClientId)}" +
                         $"&client_secret={Uri.EscapeDataString(_settings.ClientSecret)}";

        // Build an explicit request with an empty body: Quiqup's OAuth2 client-credentials
        // flow requires ALL parameters in the query string; the body must be present but empty.
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            // Empty JSON body — Quiqup's spec requires no body content for this endpoint,
            // but some server-side validation frameworks reject requests with no Content-Type.
            Content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(tokenRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "[Quiqup Auth] Token request failed. Status: {Status}, Body: {Body}",
                response.StatusCode,
                errorContent);
            throw new HttpRequestException(
                $"Quiqup authentication failed with status {response.StatusCode}.");
        }

        var tokenData = await response.Content.ReadFromJsonAsync<QuiqupTokenResponse>(
            cancellationToken: cancellationToken);

        if (tokenData is null || string.IsNullOrEmpty(tokenData.AccessToken))
        {
            throw new InvalidOperationException(
                "Quiqup token response was empty or did not contain an access_token.");
        }

        // Cache the token with a 60-second safety margin to account for clock skew / network latency.
        var cacheExpiration = TimeSpan.FromSeconds(Math.Max(tokenData.ExpiresIn - 60, 30));
        _cache.Set(TokenCacheKey, tokenData.AccessToken, cacheExpiration);

        _logger.LogInformation(
            "[Quiqup Auth] Access token acquired and cached. Effective TTL: {Ttl}s (raw expires_in: {Raw}s).",
            cacheExpiration.TotalSeconds,
            tokenData.ExpiresIn);

        return tokenData.AccessToken;
    }

    // ── Create Order ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<QuiqupDeliveryResult> CreateOrderAsync(
        Order domainOrder,
        CancellationToken cancellationToken = default)
    {
        // 1. Ensure we have a valid bearer token before constructing the request.
        var token = await GetAccessTokenAsync(cancellationToken);

        // 2. Map the domain Order to the Quiqup request payload.
        var payload = BuildOrderPayload(domainOrder);

        _logger.LogInformation(
            "[Quiqup] Creating delivery order for DuneFlame Order {OrderId} — {ItemCount} parcel(s).",
            domainOrder.Id,
            payload.Items.Count);

        // 3. Build the HTTP request with Authorization header.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(payload);

        // 4. Execute the request.
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // 5. Handle non-success status codes with structured Quiqup error logging.
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(
                response,
                context: $"POST /orders [DuneFlame Order {domainOrder.Id}]",
                cancellationToken);
        }


        // 6. Deserialize the "order" envelope.
        var envelope = await response.Content.ReadFromJsonAsync<QuiqupOrderResponseEnvelope>(
            _jsonOptions,
            cancellationToken: cancellationToken);

        if (envelope?.Order is null)
        {
            throw new InvalidOperationException(
                $"[Quiqup] POST /orders succeeded but response body was empty or missing the 'order' key " +
                $"for DuneFlame Order {domainOrder.Id}.");
        }

        var quiqupOrder = envelope.Order;

        _logger.LogInformation(
            "[Quiqup] Order created successfully. QuiqupId={QuiqupId}, Uuid={Uuid}, State={State}, TrackingUrl={TrackingUrl}",
            quiqupOrder.IdLongValue,
            quiqupOrder.Uuid,
            quiqupOrder.State,
            quiqupOrder.TrackingUrl);

        return MapToDeliveryResult(quiqupOrder);
    }

    // ── List Orders ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<QuiqupDeliveryResult>> ListOrdersAsync(
        DateTime fromDate,
        DateTime toDate,
        string stateFilter,
        int page = 1,
        int perPage = 20,
        CancellationToken cancellationToken = default)
    {
        // 1. Acquire a valid bearer token.
        var token = await GetAccessTokenAsync(cancellationToken);

        // 2. Build the query string.
        //    "filters[state]" contains brackets, so we must URI-encode the key name.
        //    Uri.EscapeDataString("filters[state]") → "filters%5Bstate%5D"
        var fromStr = fromDate.ToString("yyyy-MM-dd");
        var toStr = toDate.ToString("yyyy-MM-dd");
        var stateKey = Uri.EscapeDataString("filters[state]");
        var stateValue = Uri.EscapeDataString(stateFilter);

        var requestUrl = $"/orders" +
                         $"?from={Uri.EscapeDataString(fromStr)}" +
                         $"&to={Uri.EscapeDataString(toStr)}" +
                         $"&{stateKey}={stateValue}" +
                         $"&page={page}" +
                         $"&per_page={perPage}";

        _logger.LogInformation(
            "[Quiqup] Listing orders. From={From}, To={To}, State={State}, Page={Page}, PerPage={PerPage}.",
            fromStr, toStr, stateFilter, page, perPage);

        // 3. Build the GET request with Authorization header.
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");

        // 4. Execute.
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // 5. Handle non-success responses with structured Quiqup error extraction.
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(
                response,
                context: $"GET /orders [from={fromStr}, to={toStr}, state={stateFilter}, page={page}]",
                cancellationToken);
        }

        // 6. Deserialize the "orders" envelope.
        var envelope = await response.Content.ReadFromJsonAsync<QuiqupListOrdersEnvelope>(
            _jsonOptions,
            cancellationToken: cancellationToken);

        if (envelope is null)
        {
            _logger.LogWarning(
                "[Quiqup] GET /orders returned an empty body for filter state='{State}', from={From}, to={To}.",
                stateFilter, fromStr, toStr);
            return [];
        }

        // Guard: Quiqup may send "orders": null rather than "orders": [] on empty result sets.
        var orders = envelope.Orders ?? [];

        _logger.LogInformation(
            "[Quiqup] Listed {Count} order(s) for state='{State}', from={From}, to={To}.",
            orders.Count, stateFilter, fromStr, toStr);

        // 7. Map each Infrastructure DTO → Application-layer result and return as a read-only list.
        return orders
            .Select(MapToDeliveryResult)
            .ToList()
            .AsReadOnly();
    }

    // ── Mark Ready for Collection ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<QuiqupDeliveryResult> MarkReadyForCollectionAsync(
        long quiqupOrderId,
        CancellationToken cancellationToken = default)
    {
        // ── Step 1: Acquire a valid Bearer token (shared across both HTTP calls below). ──────
        var token = await GetAccessTokenAsync(cancellationToken);

        _logger.LogInformation(
            "[Quiqup] Starting two-step Order Flow for QuiqupOrderId={QuiqupOrderId}. " +
            "Step 2 (AWB download) will be attempted before Step 3 (ready_for_collection).",
            quiqupOrderId);

        // ── Step 2: Download AWB (GET /orders/{id}/awb) ──────────────────────────────────────
        // Per the official Quiqup Order Flow, the AWB MUST be downloaded before the order can
        // be marked ready_for_collection. Skipping this step causes a 422
        // "business_account_does_not_meet_payment_requirements" from Quiqup.
        //
        // Non-blocking: if the AWB download fails (e.g., staging quirk, transient network error)
        // we log a structured warning and continue to Step 3 rather than aborting entirely.
        // This ensures the courier notification is not blocked by a label endpoint issue.
        // ─────────────────────────────────────────────────────────────────────────────────────
        var awbUrl = $"/orders/{quiqupOrderId}/awb";

        _logger.LogInformation(
            "[Quiqup Flow Step 2] Downloading AWB for QuiqupOrderId={QuiqupOrderId}. " +
            "Endpoint: GET {Url}",
            quiqupOrderId, awbUrl);

        try
        {
            using var awbRequest = new HttpRequestMessage(HttpMethod.Get, awbUrl);
            awbRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // Accept both PDF (production) and JSON error responses (non-2xx).
            awbRequest.Headers.Accept.ParseAdd("application/pdf");
            awbRequest.Headers.Accept.ParseAdd("application/json");

            // Use ResponseHeadersRead for streaming safety — we read the full payload below
            // but this avoids buffering the entire PDF in the HttpClient internal buffer first.
            using var awbResponse = await _httpClient.SendAsync(
                awbRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (awbResponse.IsSuccessStatusCode)
            {
                var awbBytes = await awbResponse.Content.ReadAsByteArrayAsync(cancellationToken);

                _logger.LogInformation(
                    "[Quiqup Flow Step 2] AWB downloaded successfully for QuiqupOrderId={QuiqupOrderId}. " +
                    "Size={Bytes} bytes. Content-Type='{ContentType}'. Proceeding to Step 3.",
                    quiqupOrderId,
                    awbBytes.Length,
                    awbResponse.Content.Headers.ContentType?.MediaType ?? "unknown");
            }
            else
            {
                // Log the raw body for diagnostics but do NOT throw — Step 3 must still run.
                var awbErrorBody = await awbResponse.Content.ReadAsStringAsync(cancellationToken);

                _logger.LogWarning(
                    "[Quiqup Flow Step 2] AWB download returned non-success for QuiqupOrderId={QuiqupOrderId}. " +
                    "Status={Status} | Body={Body}. " +
                    "Continuing to Step 3 (ready_for_collection) despite AWB failure.",
                    quiqupOrderId,
                    awbResponse.StatusCode,
                    awbErrorBody.Length > 500 ? awbErrorBody[..500] : awbErrorBody);
            }
        }
        catch (Exception awbEx)
        {
            // Network-level failure (timeout, DNS, TLS). Log and continue — do not abort.
            _logger.LogWarning(awbEx,
                "[Quiqup Flow Step 2] AWB download threw an exception for QuiqupOrderId={QuiqupOrderId}. " +
                "Continuing to Step 3 (ready_for_collection) despite exception.",
                quiqupOrderId);
        }

        // ── Step 3: Mark Ready for Collection (PUT /orders/{id}/ready_for_collection) ────────
        // Called immediately after the AWB step (regardless of its outcome) to match the
        // official Quiqup Order Flow sequence. The prior AWB request satisfies Quiqup's
        // internal state machine requirement and prevents the 422 payment requirements error.
        // ─────────────────────────────────────────────────────────────────────────────────────
        var readyUrl = $"/orders/{quiqupOrderId}/ready_for_collection";

        _logger.LogInformation(
            "[Quiqup Flow Step 3] Marking QuiqupOrderId={QuiqupOrderId} as ready_for_collection. " +
            "Endpoint: PUT {Url}",
            quiqupOrderId, readyUrl);

        // Re-acquire token defensively — Step 2 may have taken time close to the TTL boundary.
        // GetAccessTokenAsync returns the cached token instantly if still valid.
        var freshToken = await GetAccessTokenAsync(cancellationToken);

        using var readyRequest = new HttpRequestMessage(HttpMethod.Put, readyUrl);
        readyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshToken);
        readyRequest.Headers.Accept.ParseAdd("application/json");
        // Per the Quiqup spec, this endpoint takes no request body.

        var readyResponse = await _httpClient.SendAsync(readyRequest, cancellationToken);

        if (!readyResponse.IsSuccessStatusCode)
        {
            // Structured Quiqup api_error extraction and throw.
            await HandleApiErrorAsync(
                readyResponse,
                context: $"PUT /orders/{quiqupOrderId}/ready_for_collection [after AWB step]",
                cancellationToken);
        }

        // Deserialize the updated order envelope returned by ready_for_collection.
        var envelope = await readyResponse.Content.ReadFromJsonAsync<QuiqupOrderResponseEnvelope>(
            _jsonOptions,
            cancellationToken: cancellationToken);

        if (envelope?.Order is null)
        {
            throw new InvalidOperationException(
                $"[Quiqup Flow Step 3] PUT /orders/{quiqupOrderId}/ready_for_collection succeeded " +
                $"but the response body was empty or missing the 'order' key.");
        }

        _logger.LogInformation(
            "[Quiqup Flow Step 3] QuiqupOrderId={QuiqupOrderId} successfully transitioned to state '{State}'. " +
            "Two-step Order Flow completed.",
            quiqupOrderId,
            envelope.Order.State);

        // Map Infrastructure DTO → Application-layer result.
        return MapToDeliveryResult(envelope.Order);
    }

    // ── Download AWB Label ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<byte[]> DownloadOrderLabelAsync(
        long quiqupOrderId,
        CancellationToken cancellationToken = default)
    {
        // 1. Acquire a valid bearer token.
        var token = await GetAccessTokenAsync(cancellationToken);

        _logger.LogInformation(
            "[Quiqup] Downloading AWB label PDF for order {QuiqupOrderId}.",
            quiqupOrderId);

        // 2. Build the GET request.
        //    No Accept header override — Quiqup returns application/pdf regardless.
        var requestUrl = $"/order_label/{quiqupOrderId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 3. Execute with HttpCompletionOption.ResponseHeadersRead for streaming safety,
        //    then read the full binary payload into memory.
        var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        // 4. Error handling — this endpoint is unique: success returns application/pdf,
        //    but error responses (non-2xx) still use the standard api_error JSON structure.
        //    We inspect Content-Type before routing to HandleApiErrorAsync to avoid
        //    a JsonException when the server returns a plain-text or HTML error page.
        if (!response.IsSuccessStatusCode)
        {
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                // Structured Quiqup api_error — extract and log the full detail.
                await HandleApiErrorAsync(
                    response,
                    context: $"GET /order_label/{quiqupOrderId}",
                    cancellationToken);
            }

            // Non-JSON error body (unlikely but defensive) — read raw and throw.
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "[Quiqup] GET /order_label/{QuiqupOrderId} failed. " +
                "Status: {Status} | ContentType: {ContentType} | Body: {Body}",
                quiqupOrderId,
                response.StatusCode,
                contentType,
                rawBody);

            throw new HttpRequestException(
                $"Quiqup AWB label download failed with status {response.StatusCode} " +
                $"for order {quiqupOrderId}. Body: {rawBody}");
        }

        // 5. Read the binary PDF payload.
        var pdfBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        _logger.LogInformation(
            "[Quiqup] AWB label PDF downloaded for order {QuiqupOrderId}. Size: {Bytes} bytes.",
            quiqupOrderId,
            pdfBytes.Length);

        return pdfBytes;
    }

    // ── Get Order By ID ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<QuiqupDeliveryResult> GetOrderByIdAsync(
        long quiqupOrderId,
        CancellationToken cancellationToken = default)
    {
        // 1. Acquire a valid bearer token.
        var token = await GetAccessTokenAsync(cancellationToken);

        _logger.LogInformation(
            "[Quiqup] Retrieving order details for order {QuiqupOrderId}.",
            quiqupOrderId);

        // 2. Build the GET request.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/orders/{quiqupOrderId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");

        // 3. Execute.
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // 4. Handle non-success responses.
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(
                response,
                context: $"GET /orders/{quiqupOrderId}",
                cancellationToken);
        }

        // 5. Deserialize the "order" envelope.
        var envelope = await response.Content.ReadFromJsonAsync<QuiqupOrderResponseEnvelope>(
            _jsonOptions,
            cancellationToken: cancellationToken);

        if (envelope?.Order is null)
        {
            throw new InvalidOperationException(
                $"[Quiqup] GET /orders/{quiqupOrderId} succeeded but the response body " +
                $"was empty or missing the 'order' key.");
        }

        _logger.LogInformation(
            "[Quiqup] Order {QuiqupOrderId} retrieved. State='{State}', DeliveryAttempts={Attempts}.",
            quiqupOrderId,
            envelope.Order.State,
            envelope.Order.ItemQuantityCount);

        // 6. Map and return.
        return MapToDeliveryResult(envelope.Order);
    }

    // ── Update Pending Order Payment ──────────────────────────────────────────

    /// <inheritdoc />
    public async Task UpdatePendingOrderPaymentAsync(
        long quiqupOrderId,
        string paymentMode,
        double paymentAmount,
        CancellationToken cancellationToken = default)
    {
        // Guard: enforce Quiqup's documented payment_mode / payment_amount business rule
        // BEFORE making any network call, so callers get immediate, actionable feedback.
        if (paymentMode == "pre_paid")
        {
            if (paymentAmount != 0.0)
                throw new ArgumentException(
                    $"Quiqup requires payment_amount to be exactly 0.0 when payment_mode is 'pre_paid'. " +
                    $"Received: {paymentAmount}.",
                    nameof(paymentAmount));
        }
        else
        {
            if (paymentAmount <= 0)
                throw new ArgumentException(
                    $"Quiqup requires a positive payment_amount for payment_mode '{paymentMode}'. " +
                    $"Received: {paymentAmount}.",
                    nameof(paymentAmount));
        }

        // 1. Acquire a valid bearer token.
        var token = await GetAccessTokenAsync(cancellationToken);

        _logger.LogInformation(
            "[Quiqup] Updating pending order {QuiqupOrderId} — PaymentMode='{Mode}', PaymentAmount={Amount}.",
            quiqupOrderId, paymentMode, paymentAmount);

        // 2. Build the PUT request with the JSON payload.
        var payload = new QuiqupUpdateOrderRequest
        {
            PaymentMode = paymentMode,
            PaymentAmount = paymentAmount
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/orders/{quiqupOrderId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(payload);

        // 3. Execute.
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // 4. Handle non-success responses (e.g., 422 if the order is no longer in pending state).
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(
                response,
                context: $"PUT /orders/{quiqupOrderId} [payment_mode={paymentMode}, payment_amount={paymentAmount}]",
                cancellationToken);
        }

        // 5. Quiqup returns an empty {} body on success — no deserialization needed.
        _logger.LogInformation(
            "[Quiqup] Order {QuiqupOrderId} payment updated successfully.",
            quiqupOrderId);
    }

    // ── Add Parcel to Pending Order ───────────────────────────────────────────

    /// <inheritdoc />
    public async Task AddParcelToPendingOrderAsync(
        long quiqupOrderId,
        string parcelName,
        CancellationToken cancellationToken = default)
    {
        // 1. Acquire a valid bearer token.
        var token = await GetAccessTokenAsync(cancellationToken);

        _logger.LogInformation(
            "[Quiqup] Adding parcel '{ParcelName}' to pending order {QuiqupOrderId}.",
            parcelName, quiqupOrderId);

        // 2. Build the POST request with the single-field JSON payload.
        var payload = new QuiqupAddParcelRequest { Name = parcelName };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"/orders/{quiqupOrderId}/parcels");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(payload);

        // 3. Execute.
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // 4. Handle non-success responses (e.g., 422 if the order is no longer pending).
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(
                response,
                context: $"POST /orders/{quiqupOrderId}/parcels [name='{parcelName}']",
                cancellationToken);
        }

        // 5. Quiqup returns an empty {} body on success — no deserialization needed.
        _logger.LogInformation(
            "[Quiqup] Parcel '{ParcelName}' added to order {QuiqupOrderId} successfully.",
            parcelName, quiqupOrderId);
    }

    // ── Remove Parcel from Pending Order ──────────────────────────────────────

    /// <inheritdoc />
    public async Task RemoveParcelFromPendingOrderAsync(
        long quiqupOrderId,
        string quiqupParcelId,
        CancellationToken cancellationToken = default)
    {
        // 1. Acquire a valid bearer token.
        var token = await GetAccessTokenAsync(cancellationToken);

        _logger.LogInformation(
            "[Quiqup] Removing parcel {ParcelId} from pending order {QuiqupOrderId}.",
            quiqupParcelId, quiqupOrderId);

        // 2. Build the DELETE request.
        //    HttpClient.DeleteAsync() does not support custom headers, so we use
        //    HttpRequestMessage + SendAsync to attach the Authorization header.
        var requestUrl = $"/orders/{quiqupOrderId}/parcels/{quiqupParcelId}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");

        // 3. Execute.
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // 4. Handle non-success responses.
        //    Notable cases: 422 when removing the last parcel (Quiqup enforces minimum one),
        //    422 when the order is no longer pending, 404 when the parcel doesn't exist.
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(
                response,
                context: $"DELETE /orders/{quiqupOrderId}/parcels/{quiqupParcelId}",
                cancellationToken);
        }

        // 5. Quiqup returns an empty {} body on success — no deserialization needed.
        _logger.LogInformation(
            "[Quiqup] Parcel {ParcelId} removed from order {QuiqupOrderId} successfully.",
            quiqupParcelId, quiqupOrderId);
    }

    // ── Cancel Order ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<QuiqupDeliveryResult> CancelOrderAsync(
        long quiqupOrderId,
        CancellationToken cancellationToken = default)
    {
        // 1. Acquire a valid bearer token.
        var token = await GetAccessTokenAsync(cancellationToken);

        _logger.LogInformation(
            "[Quiqup] Cancelling order {QuiqupOrderId}.",
            quiqupOrderId);

        // 2. Build the PUT request.
        //    The batch endpoint always expects a JSON body even for a single order.
        var payload = new QuiqupCancelOrdersRequest { OrderIds = new List<long> { quiqupOrderId } };

        using var request = new HttpRequestMessage(HttpMethod.Put, "/orders/batch/set_cancelled");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = JsonContent.Create(payload);

        // 3. Execute.
        var response = await _httpClient.SendAsync(request, cancellationToken);

        // 4. Handle non-success responses.
        if (!response.IsSuccessStatusCode)
        {
            await HandleApiErrorAsync(
                response,
                context: $"PUT /orders/batch/set_cancelled [order_id={quiqupOrderId}]",
                cancellationToken);
        }

        // 5. Deserialize the raw JSON array returned by this endpoint.
        //    Unlike other endpoints, there is NO outer "order" or "orders" envelope —
        //    the response is a bare array: [...]. We read directly as List<QuiqupOrderResponse>.
        //    Note: nested contact-point "id" fields arrive as quoted strings on this endpoint
        //    (e.g. "id": "1307392"). The _jsonOptions.NumberHandling setting handles this.
        var orders = await response.Content.ReadFromJsonAsync<List<QuiqupOrderResponse>>(
            _jsonOptions,
            cancellationToken: cancellationToken);

        if (orders is null || orders.Count == 0)
        {
            throw new InvalidOperationException(
                $"[Quiqup] PUT /orders/batch/set_cancelled succeeded for order {quiqupOrderId} " +
                $"but the response array was empty.");
        }

        // 6. Extract the matching order from the batch result (defensive: match by ID).
        var cancelledOrder = orders.FirstOrDefault(o => o.IdLongValue == quiqupOrderId)
                          ?? orders[0];

        _logger.LogInformation(
            "[Quiqup] Order {QuiqupOrderId} cancelled. State='{State}'.",
            quiqupOrderId,
            cancelledOrder.State);

        // 7. Map and return.
        return MapToDeliveryResult(cancelledOrder);
    }

    // ── Private Helpers ───────────────────────────────────────────────────────




    /// <summary>
    /// Maps a domain <see cref="Order"/> entity to the <see cref="QuiqupCreateOrderRequest"/> payload.
    /// </summary>
    private QuiqupCreateOrderRequest BuildOrderPayload(Order domainOrder)
    {
        // ── Diagnostic log ─────────────────────────────────────────────────────
        // This log surfaces in Cloud Run logs and confirms whether EF Core eagerly
        // loaded the navigation graph before CreateOrderAsync was called.
        // If ItemCount=0 → Include(o => o.Items) is missing at the call site.
        // If HasVariant=False → ThenInclude(i => i.ProductVariant) is missing.
        // If HasCategory=False → ThenInclude(pv => pv.Product).ThenInclude(p => p.Category) is missing.
        _logger.LogInformation(
            "[Quiqup] BuildOrderPayload — OrderId={OrderId} | ItemCount={ItemCount} | " +
            "PaymentMethod={PaymentMethod} | TotalAmount={TotalAmount} | " +
            "HasUser={HasUser} | " +
            "Items=[{ItemDetails}]",
            domainOrder.Id,
            domainOrder.Items?.Count ?? 0,
            domainOrder.PaymentMethod,
            domainOrder.TotalAmount,
            domainOrder.ApplicationUser is not null,
            string.Join("; ", (domainOrder.Items ?? []).Select(i =>
                $"'{i.ProductName}' qty={i.Quantity} " +
                $"variant={i.ProductVariant is not null} " +
                $"weightKg={i.ProductVariant?.WeightKg?.ToString() ?? "null"} " +
                $"category={i.ProductVariant?.Product?.Category?.Slug ?? "null"}"
            ))
        );

        // --- Origin: DuneFlame dispatch warehouse (driven by QuiqupSettings) ---
        var origin = new QuiqupContactPoint
        {
            ContactName = _settings.WarehouseContactName,
            ContactPhone = _settings.WarehouseContactPhone,
            Address = new QuiqupAddress
            {
                Address1 = _settings.WarehouseAddress,
                Town = _settings.WarehouseTown,
                Country = "UAE"
            }
        };

        // --- Destination: customer shipping address ---
        var (street, town, address2) = ParseShippingAddress(domainOrder.ShippingAddress);

        var customerName = BuildContactName(domainOrder.ApplicationUser);
        var customerPhone = domainOrder.ApplicationUser?.PhoneNumber ?? string.Empty;

        var destination = new QuiqupDestination
        {
            ContactName = customerName,
            ContactPhone = customerPhone,
            ShareTracking = true,
            Address = new QuiqupAddress
            {
                Address1 = street,
                Address2 = address2,
                Town = town,
                Country = "UAE"
            }
        };

        // --- Items: one parcel entry per order line item ---
        // Weight source priority:
        //   1. item.ProductVariant.WeightKg > 0 — explicit per-variant weight from the DB.
        //   2. Category slug fallback — smart per-category default (requires
        //      .ThenInclude(pv => pv.Product).ThenInclude(p => p.Category) in callers).
        //   3. 0.5 kg global default — when category is also unavailable.
        var parcels = domainOrder.Items
            .Select(item =>
            {
                // Build a descriptive parcel name that surfaces in Quiqup's "Products" column.
                // Format: "Ethiopia Yirgacheffe - Medium Roast (Espresso)"
                var name = string.IsNullOrWhiteSpace(item.ProductName)
                    ? "DuneFlame Parcel"
                    : item.ProductName;

                if (!string.IsNullOrWhiteSpace(item.SelectedRoastLevelName))
                    name += $" - {item.SelectedRoastLevelName}";

                if (!string.IsNullOrWhiteSpace(item.SelectedGrindTypeName))
                    name += $" ({item.SelectedGrindTypeName})";

                // Weight: use variant's real weight when set; otherwise resolve from category.
                double unitWeightKg;
                if (item.ProductVariant?.WeightKg > 0)
                {
                    unitWeightKg = item.ProductVariant.WeightKg.Value;
                }
                else
                {
                    var category = item.ProductVariant?.Product?.Category;
                    unitWeightKg = GetFallbackWeightKg(category);

                    _logger.LogDebug(
                        "[Quiqup] OrderItem '{Name}' — no explicit variant weight. " +
                        "Category='{Category}', FallbackWeight={Weight} kg/unit.",
                        name,
                        category?.Slug ?? "(unknown)",
                        unitWeightKg);
                }

                // Safety minimum: Quiqup rejects 0-weight parcels.
                // item.ProductVariant?.WeightKg ?? 0.250 — null/zero → 250 g floor.
                double totalWeightKg = Math.Max(unitWeightKg * item.Quantity, 0.250);

                return new QuiqupParcelItem
                {
                    Name = name,
                    Quantity = item.Quantity,
                    WeightKg = totalWeightKg
                };
            })
            .ToList();

        // Guard: Quiqup requires at least one item.
        if (parcels.Count == 0)
        {
            parcels.Add(new QuiqupParcelItem
            {
                Name = "DuneFlame Parcel",
                Quantity = 1,
                WeightKg = GetFallbackWeightKg(null)
            });
        }

        // --- Payment mode & amount ---
        // Quiqup spec §3: pre_paid → amount must be 0 (Stripe already collected payment).
        //                 paid_on_delivery → amount must be > 0 (courier collects at door).
        // We use Math.Round(..., MidpointRounding.AwayFromZero) to convert decimal AED
        // to the nearest whole dirham integer as required by the Quiqup API.
        var isCod = domainOrder.PaymentMethod == PaymentMethod.CashOnDelivery;
        var paymentMode = isCod ? "paid_on_delivery" : "pre_paid";
        var paymentAmount = isCod
            ? (double)Math.Round(domainOrder.TotalAmount, 0, MidpointRounding.AwayFromZero)
            : 0d;

        _logger.LogInformation(
            "[Quiqup] Order {OrderId} — PaymentMode={Mode}, PaymentAmount={Amount} AED.",
            domainOrder.Id, paymentMode, paymentAmount);

        return new QuiqupCreateOrderRequest
        {
            Kind = "partner_next_day",
            PaymentMode = paymentMode,
            PaymentAmount = paymentAmount,
            PartnerOrderId = domainOrder.Id.ToString(),
            Origin = origin,
            Destination = destination,
            Items = parcels
        };
    }

    /// <summary>
    /// Resolves the fallback parcel weight (kg) from the product's <see cref="Category"/> slug.
    /// Matches are substring-based and case-insensitive so minor slug variations are absorbed.
    /// </summary>
    /// <param name="category">
    /// The product's category, or <c>null</c> when the navigation graph was not eagerly loaded.
    /// </param>
    /// <returns>Fallback weight in kilograms per unit.</returns>
    private static double GetFallbackWeightKg(Category? category)
    {
        // Category.Slug is always lowercase and URL-safe (e.g. "coffee-machines", "grinders").
        // We use Contains() so partial slugs and future sub-categories also match correctly.
        var slug = (category?.Slug ?? string.Empty).ToLowerInvariant();

        return slug switch
        {
            // Heavy appliances
            var s when s.Contains("coffee-machine")
                    || s.Contains("espresso-machine")
                    || s.Contains("machine") => 6.0,

            // Counter-top electric grinders
            var s when s.Contains("grinder") => 3.0,

            // Manual brewing — V60, AeroPress, kettles, etc.
            var s when s.Contains("brew") => 0.6,

            // Small accessories — cups, spoons, tampers
            var s when s.Contains("accessor") => 0.2,

            // Cleaning powders, tablets, brushes
            var s when s.Contains("clean")
                    || s.Contains("maintenance") => 0.5,

            // Default: specialty coffee bags, unknown categories
            _ => 0.5
        };
    }

    /// <summary>
    /// Parses the stored <c>Order.ShippingAddress</c> string
    /// (format from AddressDto.ToString(): "{Street}, {City}, {State} {PostalCode}, {Country}")
    /// into address1 (street), town (city), and address2 (state + postal code) for Quiqup.
    /// Falls back gracefully when the format is unexpected.
    /// </summary>
    private static (string Street, string Town, string? Address2) ParseShippingAddress(string shippingAddress)
    {
        if (string.IsNullOrWhiteSpace(shippingAddress))
            return (string.Empty, "Dubai", null);

        // Format: "{Street}, {City}, {State} {PostalCode}, {Country}"
        var parts = shippingAddress.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var street = parts.Length > 0 ? parts[0] : shippingAddress;
        var town = parts.Length > 1 ? parts[1] : "Dubai";
        // parts[2] = "State PostalCode" — useful as address2 for courier context.
        var address2 = parts.Length > 2 ? parts[2] : null;

        return (street, town, string.IsNullOrWhiteSpace(address2) ? null : address2);
    }

    /// <summary>
    /// Builds a display name for the customer contact from the <see cref="ApplicationUser"/> entity.
    /// </summary>
    private static string BuildContactName(ApplicationUser? user)
    {
        if (user is null) return "Customer";

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "Customer" : fullName;
    }

    /// <summary>
    /// Maps an Infrastructure <see cref="QuiqupOrderResponse"/> DTO to the
    /// Application-layer <see cref="QuiqupDeliveryResult"/> record.
    /// Centralising the mapping here ensures both <c>CreateOrderAsync</c> and
    /// <c>ListOrdersAsync</c> produce identical, consistent results.
    /// </summary>
    private static QuiqupDeliveryResult MapToDeliveryResult(QuiqupOrderResponse order) =>
        new(
            QuiqupOrderId: order.IdLongValue,
            QuiqupUuid: order.Uuid,
            TrackingUrl: order.TrackingUrl,
            DestinationTrackingUrl: order.Destination?.TrackingUrl ?? string.Empty,
            State: order.State,
            PartnerOrderId: order.PartnerOrderId
        );

    /// <summary>
    /// Reads and logs the Quiqup <c>api_error</c> structure from a non-success response,
    /// then throws a clean <see cref="HttpRequestException"/>.
    /// </summary>
    /// <param name="response">The failed HTTP response to read from.</param>
    /// <param name="context">A short description of the failing operation for log messages.</param>
    /// <param name="cancellationToken">Propagates cancellation.</param>
    private async Task HandleApiErrorAsync(
        HttpResponseMessage response,
        string context,
        CancellationToken cancellationToken)
    {
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        QuiqupApiError? apiError = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<QuiqupApiErrorEnvelope>(rawBody, _jsonOptions);
            apiError = envelope?.ApiError;
        }
        catch
        {
            // Body could not be parsed as a structured error — fall through to raw-body logging.
        }

        if (apiError is not null)
        {
            _logger.LogError(
                "[Quiqup] Request failed. Context: {Context} | " +
                "Status: {Status} | ErrorId: {ErrorId} | Code: {Code} | " +
                "Message: {Message} | Human: {Human} | Description: {Description}",
                context,
                response.StatusCode,
                apiError.Id,
                apiError.Code,
                apiError.Message,
                apiError.Human,
                apiError.Description);

            if (apiError.AttributeErrors?.Count > 0)
            {
                _logger.LogError(
                    "[Quiqup] Attribute validation errors. Context: {Context} | Errors: {Errors}",
                    context,
                    JsonSerializer.Serialize(apiError.AttributeErrors));
            }

            throw new HttpRequestException(
                $"Quiqup request failed ({(int)response.StatusCode}): {apiError}");
        }

        // Fallback: log the raw body when the error shape is unrecognised.
        _logger.LogError(
            "[Quiqup] Request failed. Context: {Context} | Status: {Status} | Raw body: {Body}",
            context,
            response.StatusCode,
            rawBody);

        throw new HttpRequestException(
            $"Quiqup request failed with status {response.StatusCode}. Context: {context}. Body: {rawBody}");
    }
}
