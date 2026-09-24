using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Commands.PreparerFields;

/// <summary>Coloca un campo del preparador sobre el documento (Draft/Ready). Coordenadas normalizadas [0..1].</summary>
public sealed record PlacePreparerFieldCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label
);
