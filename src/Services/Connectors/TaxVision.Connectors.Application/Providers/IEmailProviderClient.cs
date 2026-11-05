using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Providers;

/// <summary>Lanzada por <see cref="IEmailProviderClient"/> ante cualquier fallo — igual criterio que OAuthProviderException (Fase 4): permite envolver la llamada en circuit breaker.</summary>
public sealed class EmailProviderException(
    string message,
    Exception? innerException = null,
    bool requiresReconnect = false,
    bool isNotFound = false
) : Exception(message, innerException)
{
    /// <summary>No se pudo obtener un access token porque la cuenta necesita reconectarse (ver <c>OAuthTokenErrorCodes.RequiresReconnect</c>).</summary>
    public bool RequiresReconnect { get; } = requiresReconnect;

    /// <summary>El proveedor dice que el mensaje no existe (borrado o expurgado): reintentar no lo trae.</summary>
    public bool IsNotFound { get; } = isNotFound;
}

/// <summary>
/// Un client por proveedor (Gmail, Graph) para leer el mailbox — nunca decide guardar nada,
/// solo expone metadata. Alcance Inbox-only forzado acá (D1, §34.5): nunca sincroniza el
/// mailbox completo, aunque el proveedor lo permita por default.
/// </summary>
public interface IEmailProviderClient
{
    ProviderCode ProviderCode { get; }

    /// <param name="accountId">Cuenta del provider a consultar.</param>
    /// <param name="tenantId">Rate Limit Fase 0.3 — dueño de <paramref name="accountId"/>, para el cupo per-tenant de <see cref="IProviderRateLimiter"/> (el caller ya lo tiene, cargó la cuenta para verificar tenant).</param>
    /// <param name="sinceCursor">HistoryId (Gmail) o deltaLink (Graph) opaco — null para el primer sync.</param>
    /// <param name="firstSyncFromUtc">
    /// Sin cursor (primer sync o estado vencido en el proveedor), solo entra lo recibido desde aquí: el
    /// buzón se sincroniza desde que se conectó, no su historia (igual que Gmail, que siembra con el
    /// historyId del watch y lo ignora).
    /// </param>
    Task<HistoryPage> GetHistoryAsync(
        Guid accountId,
        Guid tenantId,
        string? sinceCursor,
        DateTime? firstSyncFromUtc = null,
        CancellationToken ct = default
    );

    /// <summary>
    /// Ids del INBOX recibidos en [fromUtc, toUtc), por páginas, sin tocar el cursor ni usar el history/delta
    /// (su retención no limita la ventana). Solo para la re-sincronización (CON-07).
    /// </summary>
    Task<InboxMessageIdPage> ListInboxMessageIdsAsync(
        Guid accountId,
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        string? pageToken,
        CancellationToken ct = default
    );

    Task<RawMessage> GetMessageAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Metadata de varios mensajes. Por defecto pide uno por uno; IMAP lo resuelve en un solo FETCH.
    /// Un mensaje que falla queda con su error y no corta a los demás.
    /// </summary>
    async Task<IReadOnlyList<ProviderMessageFetch>> GetMessagesAsync(
        Guid accountId,
        Guid tenantId,
        IReadOnlyList<string> providerMessageIds,
        CancellationToken ct = default
    )
    {
        var results = new List<ProviderMessageFetch>(providerMessageIds.Count);
        foreach (var id in providerMessageIds)
        {
            try
            {
                results.Add(ProviderMessageFetch.Found(id, await GetMessageAsync(accountId, tenantId, id, ct)));
            }
            catch (EmailProviderException ex)
            {
                results.Add(ProviderMessageFetch.Failed(id, ex));
            }
        }

        return results;
    }

    /// <summary>Fetch completo bajo demanda (Fase 8, format=full/body select) — nunca se llama desde el pipeline de webhooks (metadata-first), solo desde el endpoint M2M de body fetch.</summary>
    Task<MessageBody> GetMessageBodyAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct = default
    );

    /// <summary>
    /// El correo completo en MIME (RFC 822). Solo para leer avisos de no entrega (el informe va adentro
    /// del cuerpo); nunca desde el sync, que sigue siendo metadata-only.
    /// </summary>
    Task<byte[]> GetRawMessageAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct = default
    );

    Task<Stream> GetAttachmentAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        string attachmentId,
        CancellationToken ct = default
    );
}
