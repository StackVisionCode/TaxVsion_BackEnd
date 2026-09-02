namespace TaxVision.Connectors.Application.Messages;

/// <summary>
/// Selector del adjunto dentro del mensaje. Gmail rota el <c>AttachmentId</c> entre llamadas a
/// messages.get, así que NO sirve para re-ubicarlo. Preferencia: <c>PartId</c> (id de la parte MIME,
/// estable) → (<c>Filename</c>, <c>SizeBytes</c>) → <c>AttachmentId</c>. <c>PartId</c> es null en
/// correos ingeridos antes de esta fase o en proveedores que no lo exponen.
/// </summary>
public sealed record GetMessageAttachmentQuery(
    Guid TenantId,
    Guid AccountId,
    string ProviderMessageId,
    string AttachmentId,
    string Filename,
    long SizeBytes,
    string? PartId
);
