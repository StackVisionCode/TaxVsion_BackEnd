namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Metadata de un attachment tal como la arma el caller de <see cref="IncomingEmail.Create"/>
/// (el consumer de Fase 4, a partir del evento crudo de Connectors). No es una entidad
/// persistida por sí misma — <see cref="IncomingEmail.Create"/> la usa para construir los
/// <see cref="IncomingEmailAttachment"/> reales, con su propio <c>Id</c> e <c>IncomingEmailId</c>
/// y <c>DownloadStatus</c> inicial en <see cref="AttachmentDownloadStatus.NotRequested"/>.
/// </summary>
public sealed record IncomingEmailAttachmentData(
    string Filename,
    string ContentType,
    long SizeBytes,
    string ProviderAttachmentId,
    bool IsInline
);
