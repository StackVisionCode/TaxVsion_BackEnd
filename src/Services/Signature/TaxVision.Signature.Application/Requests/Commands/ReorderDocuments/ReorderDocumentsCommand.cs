namespace TaxVision.Signature.Application.Requests.Commands.ReorderDocuments;

public sealed record ReorderDocumentsCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    IReadOnlyList<Guid> OrderedDocumentIds
);
