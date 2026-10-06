using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Templates.Commands.Create;

public sealed record CreateSignatureTemplateCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    string Category,
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    Guid? BaseDocumentFileId = null,
    bool SendSealedDocumentToSigners = true,
    bool SendCertificateToSigners = false,
    bool AutoRemindersEnabled = true,
    int ReminderIntervalHours = SignatureTemplate.DefaultReminderIntervalHours,
    // F7 — defaults heredables. Audiencia Specific se guarda como slotOrders (ints).
    bool SendPartialCopyOnEachSignature = false,
    PartialCopyAudienceKind PartialCopyAudienceKind = PartialCopyAudienceKind.All,
    IReadOnlyList<int>? PartialCopyAudienceSlotOrders = null,
    bool ExpirationEnabled = true
);
