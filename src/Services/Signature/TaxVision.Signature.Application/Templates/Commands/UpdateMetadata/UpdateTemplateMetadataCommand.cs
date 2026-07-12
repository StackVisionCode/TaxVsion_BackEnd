using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Templates.Commands.UpdateMetadata;

public sealed record UpdateTemplateMetadataCommand(
    Guid TenantId,
    Guid TemplateId,
    string Title,
    string? Description,
    SignatureCategory Category
);
