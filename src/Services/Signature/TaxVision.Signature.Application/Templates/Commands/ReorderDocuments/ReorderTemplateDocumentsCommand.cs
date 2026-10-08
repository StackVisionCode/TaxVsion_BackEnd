namespace TaxVision.Signature.Application.Templates.Commands.ReorderDocuments;

public sealed record ReorderTemplateDocumentsCommand(Guid TenantId, Guid TemplateId, IReadOnlyList<Guid> DocumentIds);
