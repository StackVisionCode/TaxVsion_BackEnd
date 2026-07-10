using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Templates.Commands.PlaceField;

public sealed record PlaceTemplateFieldCommand(
    Guid TenantId,
    Guid TemplateId,
    int SlotOrder,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label,
    bool IsRequired
);

public sealed record TemplateFieldCreatedResponse(Guid Id, int SlotOrder);
