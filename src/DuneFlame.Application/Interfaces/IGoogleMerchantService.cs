using DuneFlame.Domain.Entities;

namespace DuneFlame.Application.Interfaces;

/// <summary>
/// Syncs products to Google Merchant Center via the Content API v2.1.
/// </summary>
public interface IGoogleMerchantService
{
    /// <summary>
    /// Inserts or updates a single product in Merchant Center.
    /// If the product already exists (same offerId) it is replaced (upsert semantics).
    /// </summary>
    /// <param name="product">
    /// The fully-loaded domain Product entity (must include Translations, Images, Variants, and CoffeeProfile).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SyncProductToMerchantCenterAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a product from Merchant Center (e.g. when the product is deactivated or deleted).
    /// </summary>
    /// <param name="productSlug">The slug used as the Merchant Center offerId.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteProductFromMerchantCenterAsync(string productSlug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-syncs all active products to Merchant Center.
    /// Intended for initial setup or periodic reconciliation.
    /// </summary>
    /// <param name="products">Collection of fully-loaded Product entities.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BulkSyncProductsAsync(IEnumerable<Product> products, CancellationToken cancellationToken = default);
}
