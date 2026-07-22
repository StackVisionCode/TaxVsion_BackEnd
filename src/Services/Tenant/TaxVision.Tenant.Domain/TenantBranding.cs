using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain;

/// <summary>
/// Colores de marca por tenant (Tenant_Branding_Colors_Plan.md). Cada campo es independiente y
/// nullable: <c>null</c> significa "usar el default de la empresa" (<see cref="SystemBrandingDefaults"/>)
/// para ese campo puntual, nunca "sin pintar". <see cref="ResolveBrandingPalette"/> es la única forma
/// soportada de leer los 4 colores — nunca se leen las propiedades crudas directamente fuera del
/// dominio, para no duplicar la regla de fallback en Application/Api.
/// </summary>
public partial class Tenant
{
    public HexColor? PrimaryColor { get; private set; }
    public HexColor? AccentColor { get; private set; }
    public HexColor? BackgroundColor { get; private set; }
    public HexColor? TextColor { get; private set; }

    /// <summary>
    /// Actualiza los 4 campos de forma atómica: si cualquiera de los valores no nulos tiene formato
    /// inválido, no se aplica ningún cambio (evita dejar la paleta a medio actualizar).
    /// </summary>
    public Result SetBrandingColors(
        string? primaryColorHex,
        string? accentColorHex,
        string? backgroundColorHex,
        string? textColorHex
    )
    {
        var parsed = ParseBrandingColors(primaryColorHex, accentColorHex, backgroundColorHex, textColorHex);
        if (parsed.IsFailure)
            return Result.Failure(parsed.Error);

        ApplyBrandingColors(parsed.Value);
        return Result.Success();
    }

    /// <summary>Idempotente: no falla si la paleta ya estaba en default — mismo criterio que RemoveLogo.</summary>
    public void ResetBrandingColors()
    {
        PrimaryColor = null;
        AccentColor = null;
        BackgroundColor = null;
        TextColor = null;
    }

    /// <summary>Siempre devuelve los 4 colores completos (custom o default), nunca un campo vacío.</summary>
    public BrandingPalette ResolveBrandingPalette() =>
        new(
            PrimaryColor ?? SystemBrandingDefaults.PrimaryColor,
            AccentColor ?? SystemBrandingDefaults.AccentColor,
            BackgroundColor ?? SystemBrandingDefaults.BackgroundColor,
            TextColor ?? SystemBrandingDefaults.TextColor,
            IsCustomized: PrimaryColor is not null
                || AccentColor is not null
                || BackgroundColor is not null
                || TextColor is not null
        );

    private static Result<ParsedBrandingColors> ParseBrandingColors(
        string? primaryHex,
        string? accentHex,
        string? backgroundHex,
        string? textHex
    )
    {
        var primary = ParseOptionalColor(primaryHex);
        if (primary.IsFailure)
            return Result.Failure<ParsedBrandingColors>(primary.Error);

        var accent = ParseOptionalColor(accentHex);
        if (accent.IsFailure)
            return Result.Failure<ParsedBrandingColors>(accent.Error);

        var background = ParseOptionalColor(backgroundHex);
        if (background.IsFailure)
            return Result.Failure<ParsedBrandingColors>(background.Error);

        var text = ParseOptionalColor(textHex);
        if (text.IsFailure)
            return Result.Failure<ParsedBrandingColors>(text.Error);

        return Result.Success(new ParsedBrandingColors(primary.Value, accent.Value, background.Value, text.Value));
    }

    private static Result<HexColor?> ParseOptionalColor(string? hex)
    {
        if (hex is null)
            return Result.Success<HexColor?>(null);

        var color = HexColor.Create(hex);
        return color.IsFailure ? Result.Failure<HexColor?>(color.Error) : Result.Success<HexColor?>(color.Value);
    }

    private void ApplyBrandingColors(ParsedBrandingColors colors)
    {
        PrimaryColor = colors.Primary;
        AccentColor = colors.Accent;
        BackgroundColor = colors.Background;
        TextColor = colors.Text;
    }

    private readonly record struct ParsedBrandingColors(
        HexColor? Primary,
        HexColor? Accent,
        HexColor? Background,
        HexColor? Text
    );
}
