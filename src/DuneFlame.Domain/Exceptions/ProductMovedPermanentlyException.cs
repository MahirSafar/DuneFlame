namespace DuneFlame.Domain.Exceptions;

/// <summary>
/// Thrown when a requested product slug is no longer active but exists in
/// ProductSlugHistory. The handler that catches this exception should issue
/// a 301 Moved Permanently redirect to <see cref="NewSlug"/>.
/// </summary>
public class ProductMovedPermanentlyException : Exception
{
    /// <summary>
    /// The current, active slug the client should be redirected to.
    /// </summary>
    public string NewSlug { get; }

    public ProductMovedPermanentlyException(string oldSlug, string newSlug)
        : base($"Product slug '{oldSlug}' has moved permanently to '{newSlug}'.")
    {
        NewSlug = newSlug;
    }
}
