namespace TaxVision.Connectors.Api.Requests;

/// <summary>
/// Body de <c>POST /connectors/messages/{id}/attachments/{attachmentId}</c>. Lleva
/// <c>Filename</c>/<c>SizeBytes</c> porque el <c>attachmentId</c> del route es efímero (Gmail lo rota
/// entre fetches) — el selector estable del adjunto dentro del mensaje es (filename, size).
/// </summary>
public sealed record GetMessageAttachmentRequest(
    Guid TenantId,
    Guid AccountId,
    string Filename,
    long SizeBytes,
    string? PartId
);
