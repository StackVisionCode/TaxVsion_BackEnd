using TaxVision.Signature.Domain.Requests;

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
    bool GenerateCertificate
);
