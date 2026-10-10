namespace TaxVision.Signature.Application.Requests.Commands.AddDocument;

public sealed record AddDocumentCommand(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid OriginalFileId,
    string Title,
    string? Note,
    // F9 — page count del preflight. El aggregate lo usa después para decidir si reemplazar el PDF
    // invalida los campos; aquí es solo persistencia.
    int? PageCount = null
);
