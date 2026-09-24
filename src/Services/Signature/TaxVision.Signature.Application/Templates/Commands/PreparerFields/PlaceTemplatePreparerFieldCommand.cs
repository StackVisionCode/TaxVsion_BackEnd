using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Templates.Commands.PreparerFields;

/// <summary>Predefine un campo de firma del preparador en una plantilla (Draft). Coordenadas [0..1].</summary>
public sealed record PlaceTemplatePreparerFieldCommand(
    Guid TenantId,
    Guid TemplateId,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label
);

public sealed record TemplatePreparerFieldCreatedResponse(Guid Id);
