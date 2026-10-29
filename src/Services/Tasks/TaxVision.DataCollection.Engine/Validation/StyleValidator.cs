using TaxVision.DataCollection.Engine.Definitions;

namespace TaxVision.DataCollection.Engine.Validation;

internal static class StyleValidator
{
    private static readonly HashSet<string> Widths = new(
        ["full", "half", "third", "twoThirds"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> Alignments = new(["left", "center", "right"], StringComparer.Ordinal);
    private static readonly HashSet<string> TextSizes = new(
        ["caption", "ui", "body", "h3", "h2", "h1"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> TextColors = new(
        ["ink", "secondary", "muted", "brand", "accent", "success", "warning", "danger", "info"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> Backgrounds = new(
        ["none", "surface", "sunken", "brandSubtle", "accentSubtle", "sand", "sage"],
        StringComparer.Ordinal
    );
    private static readonly HashSet<int> Spacing = new([0, 2, 4, 6, 8]);

    // Matriz aprobada (P-20.1): fondo → colores de texto permitidos; lo que no está, se rechaza.
    private static readonly Dictionary<string, HashSet<string>> ApprovedContrast = new(StringComparer.Ordinal)
    {
        ["none"] = Colors("ink", "secondary", "muted", "brand", "accent", "success", "warning", "danger", "info"),
        ["surface"] = Colors("ink", "secondary", "muted", "brand", "accent", "success", "warning", "danger", "info"),
        ["sunken"] = Colors("ink", "secondary", "brand", "accent", "success", "warning", "danger", "info"),
        ["brandSubtle"] = Colors("ink", "secondary", "muted", "accent", "success", "warning", "danger", "info"),
        ["accentSubtle"] = Colors("ink", "secondary", "muted", "brand", "success", "warning", "danger", "info"),
        ["sand"] = Colors("ink", "secondary", "muted", "brand", "accent", "success", "danger", "info"),
        ["sage"] = Colors("ink", "secondary", "muted", "brand", "accent", "warning", "danger", "info"),
    };

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
            AddContrastIssue(path, issues);
    }

    // Un componente sin fondo propio (o `none`) pinta su texto sobre el fondo de la sección padre.
    internal static void ValidateAgainstSection(
        FormStyle? style,
        FormStyle? sectionStyle,
        string path,
        ICollection<ValidationIssue> issues
    )
    {
        if (style?.TextColor is null || (style.Background is not null && style.Background != "none"))
            return;
        if (!ContrastAllowed(style.TextColor, sectionStyle?.Background))
            AddContrastIssue(path, issues);
    }

    private static bool ContrastAllowed(string? color, string? background)
    {
        // Un token inválido ya se reporta como style.token_invalid.
        if (color is null || background is null || !TextColors.Contains(color) || !Backgrounds.Contains(background))
            return true;
        return ApprovedContrast.TryGetValue(background, out var allowed) && allowed.Contains(color);
    }

    private static void AddContrastIssue(string path, ICollection<ValidationIssue> issues) =>
        issues.Add(
            new ValidationIssue(
                "style.contrast_not_allowed",
                path,
                null,
                null,
                "Text and background tokens do not meet the approved contrast matrix."
            )
        );

    private static HashSet<string> Colors(params string[] colors) => new(colors, StringComparer.Ordinal);

    private static void Check(
        string? value,
        IReadOnlySet<string> allowed,
        string path,
        ICollection<ValidationIssue> issues
    )
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
