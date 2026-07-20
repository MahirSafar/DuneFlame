using DuneFlame.Domain.Enums;

namespace DuneFlame.Application.DTOs.Order;

public record OrderDto(
    Guid Id,
    Guid? UserId,
    OrderStatus Status,
    decimal TotalAmount,
    Currency Currency,
    DateTime CreatedAt,
    string ShippingAddress,
    string CustomerName,
    string CustomerEmail,
    string CustomerPhone,
    string? PaymentTransactionId,
    string? PaymentIntentId,
    string? ClientSecret,

    // ── Delivery ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Current delivery/courier state.
    /// Pending=0, InTransit=1, Delivered=2, Returned=3,
    /// ReadyForCollection=4, PickedUp=5, Cancelled=6, Failed=7.
    /// </summary>
    DeliveryStatus DeliveryStatus,

    /// <summary>
    /// Quiqup parcel tracking URL. Non-null once the order has been
    /// submitted to Quiqup successfully (Phase 4 completed).
    /// </summary>
    string? QuiqupTrackingUrl,

    List<OrderItemDto> Items
);
