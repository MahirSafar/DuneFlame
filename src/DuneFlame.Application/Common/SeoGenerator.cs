using System.Text.RegularExpressions;

namespace DuneFlame.Application.Common;

/// <summary>
/// Centralised SEO content generator.
/// All auto-generation logic lives here — used by ProductService (Create/Update),
/// DbInitializer (seed backfill), and AdminSeoController (one-time bulk backfill).
///
/// Every method is null-safe and idempotent: call them whenever you need a value
/// and they will always return a sensible, non-null string.
/// </summary>
public static partial class SeoGenerator
{
    // -------------------------------------------------------------------------
    // Configuration constants — change once, propagated everywhere
    // -------------------------------------------------------------------------

    private const int MetaDescriptionMaxLength = 155;
    private const string SiteName = "DuneFlame";
    private const string SiteNameAr = "دون فليم";

    // English: "Buy {name} in UAE | DuneFlame"
    private const string EnMetaTitleTemplate = "Buy {0} in UAE | {1}";

    // Arabic: "اشترِ {name} في الإمارات | دون فليم"
    private const string ArMetaTitlePrefix = "اشترِ ";
    private const string ArMetaTitleSuffix = " في الإمارات | ";

    // Image alt: "{name} - Premium Coffee & Equipment"
    private const string AltTextSuffix = " - Premium Coffee & Equipment";

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates a localised page title optimised for UAE search.
    /// • EN → "Buy {productName} in UAE | DuneFlame"
    /// • AR → "اشترِ {productName} في الإمارات | دون فليم"
    /// • Any other locale falls back to the English template.
    /// </summary>
    public static string GenerateMetaTitle(string productName, string languageCode)
    {
        if (string.IsNullOrWhiteSpace(productName)) return SiteName;

        return languageCode?.ToLowerInvariant() switch
        {
            "ar" => $"{ArMetaTitlePrefix}{productName}{ArMetaTitleSuffix}{SiteNameAr}",
            _    => string.Format(EnMetaTitleTemplate, productName, SiteName)
        };
    }

    /// <summary>
    /// Strips HTML tags, collapses whitespace, and truncates cleanly at a word
    /// boundary to at most <see cref="MetaDescriptionMaxLength"/> characters.
    /// Appends "..." only when text was actually cut.
    /// </summary>
    public static string GenerateMetaDescription(string rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription)) return string.Empty;

        // Strip HTML tags
        var plain = HtmlTagRegex().Replace(rawDescription, " ");

        // Collapse runs of whitespace (covers &nbsp; artefacts etc.)
        plain = WhitespaceRegex().Replace(plain, " ").Trim();

        if (plain.Length <= MetaDescriptionMaxLength)
            return plain;

        // Truncate at word boundary (never cut mid-word)
        var truncated = plain[..MetaDescriptionMaxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > MetaDescriptionMaxLength / 2)
            truncated = truncated[..lastSpace];

        return truncated.TrimEnd(',', ';', '.') + "...";
    }

    /// <summary>
    /// Generates descriptive image alt text.
    /// Result: "{productName} - Premium Coffee &amp; Equipment"
    /// </summary>
    public static string GenerateAltText(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return SiteName;
        return $"{productName}{AltTextSuffix}";
    }

    // -------------------------------------------------------------------------
    // Compiled regexes — zero allocation after first class load
    // -------------------------------------------------------------------------

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
