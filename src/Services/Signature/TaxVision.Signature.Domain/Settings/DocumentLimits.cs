using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Límites de aceptación de documentos que se pueden subir para firmar. Value Object
/// inmutable: cada modificación regresa una instancia nueva. Encapsula las reglas
/// (tamaño, páginas, MIME) para que <see cref="TenantSignatureSettings"/> no
/// conozca los umbrales duros.
/// </summary>
public sealed record DocumentLimits
{
    public const long DefaultMaxPdfBytes = 25L * 1024 * 1024; // 25 MB
    public const long DefaultMaxImageBytes = 10L * 1024 * 1024; // 10 MB
    public const int DefaultMaxPagesPerDocument = 100;
    public const long AbsoluteMaxPdfBytes = 200L * 1024 * 1024; // 200 MB techo duro
    public const int AbsoluteMaxPages = 1000;

    public long MaxPdfBytes { get; private init; }
    public long MaxImageBytes { get; private init; }
    public int MaxPagesPerDocument { get; private init; }

    private DocumentLimits() { }

    public static DocumentLimits Default() =>
        new()
        {
            MaxPdfBytes = DefaultMaxPdfBytes,
            MaxImageBytes = DefaultMaxImageBytes,
            MaxPagesPerDocument = DefaultMaxPagesPerDocument,
        };

    public Result<DocumentLimits> WithMaxPdfBytes(long value)
    {
        if (value is < 1024 or > AbsoluteMaxPdfBytes)
            return Result.Failure<DocumentLimits>(
                new Error("Signature.DocumentLimits.PdfSize", "MaxPdfBytes must be between 1 KB and 200 MB.")
            );

        return Result.Success(this with { MaxPdfBytes = value });
    }

    public Result<DocumentLimits> WithMaxImageBytes(long value)
    {
        if (value is < 1024 or > AbsoluteMaxPdfBytes)
            return Result.Failure<DocumentLimits>(
                new Error("Signature.DocumentLimits.ImageSize", "MaxImageBytes must be between 1 KB and 200 MB.")
            );

        return Result.Success(this with { MaxImageBytes = value });
    }

    public Result<DocumentLimits> WithMaxPages(int value)
    {
        if (value is < 1 or > AbsoluteMaxPages)
            return Result.Failure<DocumentLimits>(
                new Error("Signature.DocumentLimits.Pages", "MaxPagesPerDocument must be between 1 and 1000.")
            );

        return Result.Success(this with { MaxPagesPerDocument = value });
    }
}
