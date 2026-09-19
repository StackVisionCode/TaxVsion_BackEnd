using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Templates.Commands.Create;

public sealed record CreateSignatureTemplateCommand(
    Guid TenantId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    SignatureCategory Category,
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate,
    Guid? BaseDocumentFileId = null,
    bool SendSignedDocumentToSigners = true,
    bool SendCertificateToSigners = false,
    bool AutoRemindersEnabled = true,
    int ReminderIntervalHours = SignatureTemplate.DefaultReminderIntervalHours
);
