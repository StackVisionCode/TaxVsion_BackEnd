using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending;

/// <summary>Bytes ya descargados de un <see cref="OutboundAttachmentRef"/>, listos para <c>ConnectorsSendClient</c>.</summary>
public sealed record OutboundAttachmentBytes(string Filename, string ContentType, byte[] Content);

/// <summary>
/// Descarga los bytes de un set de <see cref="OutboundAttachmentRef"/> desde CloudStorage — hermano
/// de <see cref="IInlineAssetFetcher"/> (D3 Compose §11.3/§12), pero sin el cap agregado de 5MB: ese
/// límite es específico de logos CID (Fase 3.5). El cap real acá lo aplica el proveedor resuelto
/// recién al momento del envío (Connectors ya lo hace, ver <c>SendFailureReason.AttachmentTooLarge</c>).
/// </summary>
public interface IOutboundAttachmentFetcher
{
    Task<Result<IReadOnlyList<OutboundAttachmentBytes>>> FetchAllAsync(
        Guid tenantId,
        IReadOnlyList<OutboundAttachmentRef> attachments,
        CancellationToken ct
    );

    /// <summary>
    /// Igual que <see cref="FetchAllAsync"/> pero partiendo de ids pelados: resuelve nombre y
    /// content-type contra CloudStorage antes de bajar. Lo usan los correos de notificación, cuyo
    /// evento solo trae <c>AttachmentFileIds</c> — el preparador de Correspondence sí arma el ref
    /// completo porque acaba de subir el archivo.
    /// </summary>
    Task<Result<IReadOnlyList<OutboundAttachmentBytes>>> FetchByFileIdsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> fileIds,
        CancellationToken ct
    );
}
