using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Tamaño de letra con el que se estampa el contenido de un campo de texto (iniciales, fecha, texto libre).
/// Lo elige el preparador; null = el tamaño por defecto de la plataforma para ese tipo. La caja del campo es
/// el área disponible: el sellador nunca agranda la letra para llenarla, solo la achica si no cabe.
/// </summary>
public static class FieldTypography
{
    // Techos técnicos: por debajo de 6 pt deja de leerse impreso; por encima de 72 no es un campo de formulario.
    public const int MinFontSizePt = 6;
    public const int MaxFontSizePt = 72;

    /// <summary>En automático la letra ocupa este tanto del alto de la caja (el resto es aire arriba y abajo).</summary>
    public const double AutoHeightRatio = 0.75;

    /// <summary>
    /// Letra de partida de un campo: la fija del preparador o, en automático, 75 % del alto de la caja acotado
    /// al rango de la plataforma. El sellador después solo la achica si el contenido no cabe.
    /// </summary>
    public static double StartingFontSize(int? fontSizePt, double boxHeightPt, int autoMinPt, int autoMaxPt) =>
        fontSizePt ?? Math.Clamp(boxHeightPt * AutoHeightRatio, autoMinPt, autoMaxPt);

    /// <summary>Solo los campos que se estampan como texto tienen tamaño de letra.</summary>
    public static bool HasFontSize(SignatureFieldKind kind) =>
        kind is SignatureFieldKind.Initials or SignatureFieldKind.Date or SignatureFieldKind.Text;

    /// <summary>Valida el tamaño pedido; en tipos sin texto (firma, casilla) se descarta.</summary>
    public static Result<int?> Normalize(SignatureFieldKind kind, int? fontSizePt)
    {
        if (fontSizePt is null || !HasFontSize(kind))
            return Result.Success<int?>(null);
        if (fontSizePt is < MinFontSizePt or > MaxFontSizePt)
            return Result.Failure<int?>(
                new Error(
                    "Signature.Field.FontSize",
                    $"Font size must be between {MinFontSizePt} and {MaxFontSizePt} pt."
                )
            );
        return Result.Success(fontSizePt);
    }
}
