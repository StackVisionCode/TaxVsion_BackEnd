namespace TaxVision.Signature.Application.Templates.Commands.RemoveDocument;

public sealed record RemoveTemplateDocumentCommand(Guid TenantId, Guid TemplateId, Guid DocumentId);
