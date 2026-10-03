namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Imagen que el HTML referencia con <c>cid:{ContentId}</c> (hoy el logo de cabecera). Bytes ya
/// resueltos por el caller, igual que <see cref="OutboundAttachment"/>. No es un adjunto: va inline y
/// el destinatario no lo descarga.
/// </summary>
public sealed record OutboundInlineAsset(string ContentId, string ContentType, byte[] Content);
