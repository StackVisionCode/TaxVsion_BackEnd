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
    DateTime SignedAtUtc
);
