using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Providers;

/// <summary>Lanzada por <see cref="IEmailProviderClient"/> ante cualquier fallo — igual criterio que OAuthProviderException (Fase 4): permite envolver la llamada en circuit breaker.</summary>
public sealed class EmailProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Un client por proveedor (Gmail, Graph) para leer el mailbox — nunca decide guardar nada,
/// solo expone metadata. Alcance Inbox-only forzado acá (D1, §34.5): nunca sincroniza el
/// mailbox completo, aunque el proveedor lo permita por default.
/// </summary>
public interface IEmailProviderClient
{
    ProviderCode ProviderCode { get; }

    /// <param name="sinceCursor">HistoryId (Gmail) o deltaLink (Graph) opaco — null para el primer sync.</param>
    Task<HistoryPage> GetHistoryAsync(Guid accountId, string? sinceCursor, CancellationToken ct = default);

    Task<RawMessage> GetMessageAsync(Guid accountId, string providerMessageId, CancellationToken ct = default);

    /// <summary>Fetch completo bajo demanda (Fase 8, format=full/body select) — nunca se llama desde el pipeline de webhooks (metadata-first), solo desde el endpoint M2M de body fetch.</summary>
    Task<MessageBody> GetMessageBodyAsync(Guid accountId, string providerMessageId, CancellationToken ct = default);

    Task<Stream> GetAttachmentAsync(
        Guid accountId,
        string providerMessageId,
        string attachmentId,
        CancellationToken ct = default
    );
}
