using FluentValidation;

namespace DuneFlame.Application.Common;

public static class ValidationExtensions
{
    // XSS (Cross-Site Scripting) qorunması üçün sadə filtr
    public static IRuleBuilderOptions<T, string> MustBeSafeInput<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .Must(str => !ContainsHtmlTags(str))
            .WithMessage("'{PropertyName}' contains unsafe characters or HTML tags.");
    }

    private static bool ContainsHtmlTags(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // Sadə yoxlama: < və > simvolları varsa şübhəlidir
        // Daha mürəkkəb regex də istifadə oluna bilər
        return text.Contains('<') || text.Contains('>') || text.Contains("javascript:");
    }
}