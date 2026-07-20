namespace DuneFlame.Application.DTOs.Admin;

/// <summary>
/// Flat DTO optimised for the Admin Products table view.
/// Contains all columns required by the UI grid.
/// </summary>
public record AdminProductListDto(
    /// <summary>Product unique identifier.</summary>
    Guid Id,

    /// <summary>URL-friendly slug.</summary>
    string Slug,

    // ── Identity ─────────────────────────────────────────────────────────────
    /// <summary>Localised display name (resolved via Accept-Language).</summary>
    string Name,

    /// <summary>URL of the main product image, or null if none uploaded.</summary>
    string? MainImageUrl,

    // ── Taxonomy ─────────────────────────────────────────────────────────────
    Guid CategoryId,
    string CategoryName,

    Guid? BrandId,
    string? BrandName,

    // ── Pricing ──────────────────────────────────────────────────────────────
    /// <summary>Lowest variant price across all variants (base currency).</summary>
    decimal? PriceFrom,

    /// <summary>Highest variant price across all variants (base currency). Equal to PriceFrom when all variants share the same price.</summary>
    decimal? PriceTo,

    // ── Inventory ────────────────────────────────────────────────────────────
    /// <summary>Total stock quantity summed across all variants. Null when stock tracking is disabled.</summary>
    int? TotalStock,

    /// <summary>Number of distinct variants (SKUs).</summary>
    int VariantsCount,

    // ── Status ───────────────────────────────────────────────────────────────
    /// <summary>
    /// True when the product has been soft-deleted (IsDeleted = true in the entity).
    /// Editable: set to false via restore endpoint, true via delete endpoint.
    /// </summary>
    bool IsDeleted,

    // ── Timestamps ───────────────────────────────────────────────────────────
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
