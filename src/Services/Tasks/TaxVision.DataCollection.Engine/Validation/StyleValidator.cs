using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Validation;

internal static class StyleValidator
{
    private static readonly HashSet<string> Widths = new(["full", "half", "third", "twoThirds"], StringComparer.Ordinal);
    private static readonly HashSet<string> Alignments = new(["left", "center", "right"], StringComparer.Ordinal);
    private static readonly HashSet<string> TextSizes = new(["caption", "ui", "body", "h3", "h2", "h1"], StringComparer.Ordinal);
    private static readonly HashSet<string> TextColors = new(["ink", "secondary", "muted", "brand", "accent", "success", "warning", "danger", "info"], StringComparer.Ordinal);
    private static readonly HashSet<string> Backgrounds = new(["none", "surface", "sunken", "brandSubtle", "accentSubtle", "sand", "sage"], StringComparer.Ordinal);
    private static readonly HashSet<int> Spacing = new([0, 2, 4, 6, 8]);

    internal static void Validate(FormStyle? style, string path, ICollection<ValidationIssue> issues)
    {
        if (style is null)
            return;
        Check(style.Width, Widths, $"{path}/width", issues);
        Check(style.Align, Alignments, $"{path}/align", issues);
        Check(style.TextSize, TextSizes, $"{path}/textSize", issues);
        Check(style.TextColor, TextColors, $"{path}/textColor", issues);
        Check(style.Background, Backgrounds, $"{path}/background", issues);
        CheckSpacing(style.SpacingTop, $"{path}/spacingTop", issues);
        CheckSpacing(style.SpacingBottom, $"{path}/spacingBottom", issues);

        if (!ContrastAllowed(style.TextColor, style.Background))
            issues.Add(new ValidationIssue("style.contrast_not_allowed", path, null, null, "Text and background tokens do not meet the approved contrast matrix."));
    }

    private static bool ContrastAllowed(string? color, string? background)
    {
        if (color is null || background is null || background is "none" or "surface")
            return true;
        return (background, color) switch
        {
            ("sunken", "muted") => false,
            ("brandSubtle", "brand") => false,
            ("accentSubtle", "accent") => false,
            ("sand", "warning") => false,
            ("sage", "success") => false,
            _ => true,
        };
    }

    private static void Check(string? value, IReadOnlySet<string> allowed, string path, ICollection<ValidationIssue> issues)
    {
        if (value is not null && !allowed.Contains(value))
            issues.Add(new ValidationIssue("style.token_invalid", path, null, null, "Style token is not supported."));
    }

    private static void CheckSpacing(int? value, string path, ICollection<ValidationIssue> issues)
    {
        if (value is not null && !Spacing.Contains(value.Value))
            issues.Add(new ValidationIssue("style.token_invalid", path, null, null, "Spacing token is not supported."));
    }
}
