using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>
/// Descripción normalizada de un campo a estampar sobre el documento sellado. Se
/// construye en el consumer a partir del aggregate y se pasa al engine — sin depender
/// del ORM ni del dominio directamente. La coordenada está en [0..1] respecto al
/// tamaño de la página (independiente del DPI).
/// </summary>
public sealed record SealedFieldRender(
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    SignatureFieldKind Kind,
    string? Label,
    string SignerDisplayName,
    DateTime SignedAtUtc,
    // PNG de la firma del firmante (dibujada/subida, o el nombre tecleado rasterizado por el front) para
    // campos Signature. Null → fallback tipográfico. Los bytes los baja el consumer de CloudStorage, así el
    // engine queda puro (sin I/O).
    byte[]? SignatureImageBytes = null,
    // Texto que el firmante escribió en un campo Text (P4). Null/empty en los demás tipos y en los
    // campos de texto opcionales que dejó en blanco.
    string? Value = null
);
