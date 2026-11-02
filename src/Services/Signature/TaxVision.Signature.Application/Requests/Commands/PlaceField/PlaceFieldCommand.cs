using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Commands.PlaceField;

public sealed record PlaceFieldCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid SignerId,
    Guid DocumentId,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label,
    bool IsRequired,
    // Letra del estampado (iniciales/fecha/texto); null = automática según la caja.
    int? FontSizePt = null
);
