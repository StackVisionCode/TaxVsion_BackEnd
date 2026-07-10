using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Templates;

public sealed record TemplateSlotResponse(Guid Id, int Order, string Role, string DefaultLanguage);

public sealed record TemplateFieldResponse(
    Guid Id,
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

public sealed record SignatureTemplateResponse(
    Guid Id,
    Guid TenantId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    SignatureCategory Category,
    SignatureTemplateStatus Status,
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? ArchivedAtUtc,
    IReadOnlyList<TemplateSlotResponse> Slots,
    IReadOnlyList<TemplateFieldResponse> Fields
)
{
    public static SignatureTemplateResponse From(SignatureTemplate template) =>
        new(
            template.Id,
            template.TenantId,
            template.CreatedByUserId,
            template.Title,
            template.Description,
            template.Category,
            template.Status,
            template.DefaultTokenExpirationHours,
            template.RequiresSequentialSigning,
            template.RequiresConsent,
            template.GenerateCertificate,
            template.CreatedAtUtc,
            template.UpdatedAtUtc,
            template.PublishedAtUtc,
            template.ArchivedAtUtc,
            template
                .Slots.Select(s => new TemplateSlotResponse(s.Id, s.Order, s.Role.Value, s.DefaultLanguage))
                .ToList(),
            template
                .Fields.Select(f => new TemplateFieldResponse(
                    f.Id,
                    f.SlotOrder,
                    f.Kind,
                    f.Position.Page,
                    f.Position.X,
                    f.Position.Y,
                    f.Position.Width,
                    f.Position.Height,
                    f.Label,
                    f.IsRequired
                ))
                .ToList()
        );
}
