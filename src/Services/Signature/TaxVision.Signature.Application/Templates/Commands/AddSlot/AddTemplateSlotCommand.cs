using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Templates.Commands.AddSlot;

public sealed record AddTemplateSlotCommand(
    Guid TenantId,
    Guid TemplateId,
    string Role,
    string DefaultLanguage,
    SignerVerificationMethod? RequiredVerificationMethod = null
);

public sealed record TemplateSlotCreatedResponse(
    Guid Id,
    int Order,
    string Role,
    string DefaultLanguage,
    SignerVerificationMethod? RequiredVerificationMethod
);
