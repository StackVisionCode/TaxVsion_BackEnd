namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Fila de metadata para el listado paginado de mensajes de un hilo (Fase 9,
/// <c>GET /correspondence/threads/{threadId}/messages</c>, extendido en Fase 15 a inbound+outbound)
/// y para la vista de un mensaje individual (<c>GET /correspondence/messages/{id}</c>, siempre
/// <see cref="MessageDirection.Inbound"/>). A propósito nunca incluye el body (HTML/texto) — ese es
/// <see cref="GetMessageBodyHandler"/>, un endpoint dedicado (Fase 5) que pide el contenido a
/// Connectors en vivo; este DTO es puro metadata ya persistida.
///
/// <para>
/// Un solo record para ambas direcciones, no dos DTOs separados (Fase 15, WHY): la UI arma UNA
/// lista cronológica mezclada — separar el shape forzaría un discriminated union en el cliente
/// para algo que ya resuelve <see cref="Direction"/>. El costo es un puñado de campos nulos por
/// dirección: <see cref="From"/>/<see cref="FromDisplayName"/>/<see cref="Snippet"/>/
/// <see cref="BodyStatus"/> solo aplican a <see cref="MessageDirection.Inbound"/> (un
/// <c>Draft</c> Sent no tiene remitente propio ni body-fetch-on-demand, es texto ya persistido en
/// el tenant); <see cref="ToAddresses"/> solo aplica a <see cref="MessageDirection.Outbound"/> (un
/// <c>IncomingEmail</c> no expone sus destinatarios acá, eso ya lo sabe el customer que lo mandó).
/// <see cref="HasAttachments"/>/<see cref="AttachmentCount"/> sí aplican a ambas direcciones — un
/// <c>Draft</c> ya trae sus adjuntos persistidos (<c>Draft.Attachments</c>), no hace falta llamar a
/// nadie para saberlo.
/// </para>
/// </summary>
public sealed record MessageSummary(
    Guid MessageId,
    MessageDirection Direction,
    string? From,
    string? FromDisplayName,
    string Subject,
    string? Snippet,
    IReadOnlyList<string>? ToAddresses,
    DateTime OccurredAtUtc,
    bool HasAttachments,
    int AttachmentCount,
    string? BodyStatus
);
