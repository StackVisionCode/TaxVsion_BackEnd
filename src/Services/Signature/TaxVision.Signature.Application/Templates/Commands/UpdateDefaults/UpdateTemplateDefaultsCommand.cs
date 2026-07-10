namespace TaxVision.Signature.Application.Templates.Commands.UpdateDefaults;

public sealed record UpdateTemplateDefaultsCommand(
    Guid TenantId,
    Guid TemplateId,
    int DefaultTokenExpirationHours,
    bool RequiresSequentialSigning,
    bool RequiresConsent,
    bool GenerateCertificate
);
