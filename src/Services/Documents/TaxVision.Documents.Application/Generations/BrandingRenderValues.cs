namespace TaxVision.Documents.Application.Generations;

/// <summary>Lo que aceptan las plantillas del branding, igual para la factura y el resumen.</summary>
internal static class BrandingRenderValues
{
    // Logo solo embebido (data:): un recurso externo lo bloquea el CSP del motor.
    public static string EmbeddedLogo(string? logoDataUri) =>
        logoDataUri is { Length: > 0 } logo && logo.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? logo
            : string.Empty;

    public static bool IsHexColor(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is 4 or 7
        && value[0] == '#'
        && value.AsSpan(1).ToArray().All(Uri.IsHexDigit);
}
