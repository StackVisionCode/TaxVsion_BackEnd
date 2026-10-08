namespace TaxVision.Signature.Application.Requests.Commands.AddDocument;

public sealed record AddDocumentCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid OriginalFileId,
    string Title,
    string? Note
);
