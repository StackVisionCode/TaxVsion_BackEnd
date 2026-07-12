using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Ubicación de un <see cref="SignatureField"/> sobre el documento en coordenadas
/// normalizadas [0..1] respecto al tamaño de la página. Independiente del DPI y de la
/// resolución del PDF, lo que evita drift entre navegadores y el motor de sellado.
/// </summary>
public sealed record FieldPosition
{
    private const double MinValue = 0d;
    private const double MaxValue = 1d;

    /// <summary>Número de página 1-indexado.</summary>
    public int Page { get; }

    /// <summary>Coordenada X en [0..1] desde el borde izquierdo.</summary>
    public double X { get; }

    /// <summary>Coordenada Y en [0..1] desde el borde superior.</summary>
    public double Y { get; }

    /// <summary>Ancho en [0..1] respecto al ancho de la página.</summary>
    public double Width { get; }

    /// <summary>Alto en [0..1] respecto al alto de la página.</summary>
    public double Height { get; }

    private FieldPosition(int page, double x, double y, double width, double height)
    {
        Page = page;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static Result<FieldPosition> Create(int page, double x, double y, double width, double height)
    {
        if (page < 1)
            return Result.Failure<FieldPosition>(new Error("Signature.FieldPosition.Page", "Page must be >= 1."));

        if (!IsInUnitRange(x) || !IsInUnitRange(y))
            return Result.Failure<FieldPosition>(
                new Error("Signature.FieldPosition.Origin", "X and Y must be within [0, 1].")
            );

        if (width is <= 0 or > MaxValue || height is <= 0 or > MaxValue)
            return Result.Failure<FieldPosition>(
                new Error("Signature.FieldPosition.Size", "Width and Height must be within (0, 1].")
            );

        if (x + width > MaxValue + 1e-6 || y + height > MaxValue + 1e-6)
            return Result.Failure<FieldPosition>(
                new Error("Signature.FieldPosition.Overflow", "The field extends outside the page bounds.")
            );

        return Result.Success(new FieldPosition(page, x, y, width, height));
    }

    private static bool IsInUnitRange(double value) => value is >= MinValue and <= MaxValue;
}
