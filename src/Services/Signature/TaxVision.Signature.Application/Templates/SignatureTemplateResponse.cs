using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Templates;

public sealed record TemplateSlotResponse(
    Guid Id,
    int Order,
    string Role,
    string DefaultLanguage,
    SignerVerificationMethod? RequiredVerificationMethod
);

public sealed record TemplateFieldResponse(
    Guid Id,
    Guid TemplateDocumentId,
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

/// <summary>Campo del preparador predefinido en la plantilla (sin slot). Se hereda al instanciar.</summary>
public sealed record TemplatePreparerFieldResponse(
    Guid Id,
    Guid TemplateDocumentId,
    SignatureFieldKind Kind,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    string? Label
);

public sealed record TemplateDocumentResponse(Guid Id, int Order, Guid FileId, string Title);

public sealed record SignatureTemplateResponse(
    Guid Id,
    Guid TenantId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    string Category,
    SignatureTemplateStatus Status,
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    bool SendSealedDocumentToSigners,
    bool SendCertificateToSigners,
    bool AutoRemindersEnabled,
    int ReminderIntervalHours,
    // F7 — defaults heredables al instanciar la Request.
    bool SendPartialCopyOnEachSignature,
    PartialCopyAudienceKind PartialCopyAudienceKind,
    IReadOnlyList<int> PartialCopyAudienceSlotOrders,
    bool ExpirationEnabled,
    bool RequiresPractitionerPin,
    Guid? BaseDocumentFileId,
    IReadOnlyList<TemplateDocumentResponse> BaseDocuments,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? ArchivedAtUtc,
    IReadOnlyList<TemplateSlotResponse> Slots,
    IReadOnlyList<TemplateFieldResponse> Fields,
    IReadOnlyList<TemplatePreparerFieldResponse> PreparerFields
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
            template.SendSealedDocumentToSigners,
            template.SendCertificateToSigners,
            template.AutoRemindersEnabled,
            template.ReminderIntervalHours,
            template.SendPartialCopyOnEachSignature,
            template.PartialCopyAudienceKind,
            template.PartialCopyAudienceSlotOrders.OrderBy(x => x).ToList(),
            template.ExpirationEnabled,
            template.RequiresPractitionerPin,
            template.BaseDocumentFileId,
            template
                .Documents.OrderBy(document => document.Order)
                .Select(document => new TemplateDocumentResponse(
                    document.Id,
                    document.Order,
                    document.FileId,
                    document.Title
                ))
                .ToList(),
            template.CreatedAtUtc,
            template.UpdatedAtUtc,
            template.PublishedAtUtc,
            template.ArchivedAtUtc,
            template
                .Slots.Select(s => new TemplateSlotResponse(
                    s.Id,
                    s.Order,
                    s.Role.Value,
                    s.DefaultLanguage,
                    s.RequiredVerificationMethod
                ))
                .ToList(),
            template
                .Fields.Select(f => new TemplateFieldResponse(
                    f.Id,
                    f.TemplateDocumentId,
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
                .ToList(),
            template
                .PreparerFields.Select(f => new TemplatePreparerFieldResponse(
                    f.Id,
                    f.TemplateDocumentId,
                    f.Kind,
                    f.Position.Page,
                    f.Position.X,
                    f.Position.Y,
                    f.Position.Width,
                    f.Position.Height,
                    f.Label
                ))
                .ToList()
        );
}
