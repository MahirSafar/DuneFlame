using System.Text.RegularExpressions;

namespace DuneFlame.Application.Common;

public static class SlugGenerator
{
    public static string GenerateSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Convert to lowercase
        var slug = input.ToLowerInvariant();

        // Replace spaces with hyphens
        slug = Regex.Replace(slug, @"\s+", "-");

        // Remove special characters (keep only alphanumeric and hyphens)
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);

        // Remove consecutive hyphens
        slug = Regex.Replace(slug, @"-+", "-");

        // Trim hyphens from start and end
        slug = slug.Trim('-');

        return slug;
    }
}
