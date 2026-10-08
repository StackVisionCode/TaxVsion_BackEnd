namespace TaxVision.Signature.Application.Templates.Commands.AddDocument;

public sealed record AddTemplateDocumentCommand(Guid TenantId, Guid TemplateId, Guid FileId, string Title);

public sealed record TemplateDocumentCreatedResponse(Guid Id, int Order, Guid FileId, string Title);
