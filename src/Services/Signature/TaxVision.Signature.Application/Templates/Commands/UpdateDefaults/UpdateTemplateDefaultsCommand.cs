using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Templates.Commands.UpdateDefaults;

public sealed record UpdateTemplateDefaultsCommand(
    Guid TenantId,
    Guid TemplateId,
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    bool SendSealedDocumentToSigners,
    bool SendCertificateToSigners,
    bool AutoRemindersEnabled,
    int ReminderIntervalHours,
    // F7 — defaults heredables al instanciar. Audiencia Specific usa slotOrders (ints), no GUIDs.
    bool SendPartialCopyOnEachSignature,
    PartialCopyAudienceKind PartialCopyAudienceKind,
    IReadOnlyList<int>? PartialCopyAudienceSlotOrders,
    bool ExpirationEnabled
);
