namespace TaxVision.Correspondence.Application.Trash;

// Fila de la papelera. Kind distingue entrante vs enviado (endpoints de restore/purge distintos).
public sealed record TrashItem(
    Guid MessageId,
    string Kind,
    Guid? EmailThreadId,
    string Subject,
    string Counterparty,
    DateTime DeletedAtUtc,
    bool HasAttachments,
    int AttachmentCount
);
