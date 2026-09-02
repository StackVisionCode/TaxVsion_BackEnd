using BuildingBlocks.Common;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Consultada por <see cref="Ingest.RawMessageReceivedConsumer"/> (Fase 4) para el dedup por
/// <c>InternetMessageId</c> y para resolver threading (Layer 2/3 comparten la misma consulta:
/// buscar el <see cref="IncomingEmail"/> dueño de un <c>InternetMessageId</c> conocido, sea el
/// del propio dedup, el de <c>In-Reply-To</c> o el de <c>References</c>).
/// </summary>
public interface IIncomingEmailRepository
{
    /// <summary>Busca por <c>(TenantId, InternetMessageId)</c>. Devuelve <c>null</c> si no existe.</summary>
    Task<IncomingEmail?> FindByInternetMessageIdAsync(
        Guid tenantId,
        string internetMessageId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Busca por <c>(TenantId, Id)</c>. Devuelve <c>null</c> si no existe o pertenece a otro
    /// tenant — usado por <c>GetMessageBodyHandler</c> (Fase 5), nunca confía en el TenantId de
    /// la ruta/query, siempre en el del JWT.
    /// </summary>
    Task<IncomingEmail?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task AddAsync(IncomingEmail entity, CancellationToken ct = default);

    /// <summary>
    /// Fase 9 — mensajes de un hilo para el cliente final, en orden cronológico ascendente
    /// (el más viejo primero): a diferencia del listado de hilos (recencia primero, para saber
    /// dónde hay actividad nueva), leer UNA conversación se hace de arriba hacia abajo como
    /// cualquier cliente de correo. Requiere un índice propio por <c>EmailThreadId</c> — el
    /// índice de Fase 3 es <c>(TenantId, CustomerId, ReceivedAtUtc)</c>, pensado para un inbox
    /// agregado por customer, no para esta consulta por hilo.
    /// </summary>
    Task<PagedResult<IncomingEmail>> ListByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Fase 15 — variante sin paginar de <see cref="ListByThreadAsync"/>, para
    /// <c>ListThreadMessagesHandler</c>: necesita TODOS los <see cref="IncomingEmail"/> del hilo en
    /// memoria para poder fusionarlos con los <c>Draft</c> Sent del mismo hilo y paginar recién
    /// sobre el resultado ya mezclado (nunca por fuente). Seguro porque un hilo real está acotado
    /// (<see cref="Inbox.EmailThread.MessageCount"/> lo trackea) — a diferencia de, por ejemplo,
    /// "todos los mensajes de un customer", que sí crecería sin límite. Mismo índice que
    /// <see cref="ListByThreadAsync"/>.
    /// </summary>
    Task<IReadOnlyList<IncomingEmail>> ListAllByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Cantidad de correos NO leídos por hilo, para los <c>threadIds</c> pedidos (los de una página
    /// del inbox). Devuelve solo las entradas con conteo &gt; 0 — un hilo ausente del diccionario
    /// tiene 0 no-leídos. Usa <c>IX_IncomingEmails_TenantId_EmailThreadId_Unread</c> (filtrado).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetUnreadCountsByThreadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> emailThreadIds,
        CancellationToken ct = default
    );

    /// <summary>
    /// TODOS los correos de un hilo, TRACKED (sin <c>AsNoTracking</c>) — para mutar su estado
    /// leído/no-leído en bloque (<c>MarkThreadRead/Unread</c>) y persistir. Seguro por lo acotado
    /// del hilo, mismo criterio que <see cref="ListAllByThreadAsync"/> (que es read-only/no-tracking).
    /// </summary>
    Task<IReadOnlyList<IncomingEmail>> ListByThreadForUpdateAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    );

    // Papelera: entrantes borrados de un customer, más reciente primero.
    Task<PagedResult<IncomingEmail>> ListTrashedByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int page,
        int size,
        CancellationToken ct = default
    );

    // Borrado permanente (solo desde la papelera).
    void Remove(IncomingEmail entity);

    // Email dueño del adjunto con ese fileId de CloudStorage (tracked, para marcarlo Blocked al escanear).
    Task<IncomingEmail?> FindByAttachmentCloudStorageFileIdAsync(
        Guid tenantId,
        Guid cloudStorageFileId,
        CancellationToken ct = default
    );
}
