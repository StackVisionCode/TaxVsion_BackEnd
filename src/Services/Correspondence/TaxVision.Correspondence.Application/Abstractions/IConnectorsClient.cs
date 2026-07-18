using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Cliente M2M hacia Connectors.Api (<c>POST /connectors/messages/{providerMessageId}/body</c> y
/// <c>POST /connectors/messages/{providerMessageId}/attachments/{attachmentId}</c>, policy
/// <c>ServiceOnly</c>, Connectors Fase 8/9) — body y attachment bytes bajo demanda (Fase 5/8). El
/// body NUNCA se persiste en Correspondence (plan de diseño §17): esta llamada es puramente
/// pass-through hacia el caller HTTP, <see cref="MessageBodyResponse"/> se descarta apenas se
/// devuelve la respuesta. Los bytes de un attachment (Fase 8) sí se persisten, pero no acá — el
/// caller (<c>DownloadAttachmentHandler</c>) los sube a CloudStorage vía el flujo D0/D1.
/// </summary>
public interface IConnectorsClient
{
    Task<Result<MessageBodyResponse>> FetchMessageBodyAsync(
        Guid tenantId,
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Trae los bytes crudos de un attachment. El nombre/tipo de archivo NO viaja en la respuesta:
    /// Correspondence ya los tiene guardados en <c>IncomingEmailAttachment</c> desde la Fase 3
    /// (metadata que llegó con el mensaje) — pedirlos de nuevo acá sería redundante.
    /// </summary>
    Task<Result<ConnectorsAttachmentBytes>> FetchAttachmentAsync(
        Guid tenantId,
        Guid accountId,
        string providerMessageId,
        string providerAttachmentId,
        CancellationToken ct = default
    );
}

/// <summary>
/// Body de un mensaje tal cual lo devuelve Connectors. Deliberadamente no incluye los adjuntos
/// del wire contract de Connectors (<c>MessageBodyDto.Attachments</c>) — esa metadata es de la
/// Fase 7 (attachment listing), fuera de alcance acá (YAGNI).
/// </summary>
public sealed record MessageBodyResponse(
    string? HtmlBody,
    string? TextBody,
    IReadOnlyDictionary<string, string> Headers
);

/// <summary>Bytes crudos de un attachment tal cual los devuelve Connectors (respuesta binaria, no JSON).</summary>
public sealed record ConnectorsAttachmentBytes(byte[] Content);
