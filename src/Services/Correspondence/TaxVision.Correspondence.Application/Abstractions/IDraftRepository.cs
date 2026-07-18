using BuildingBlocks.Common;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Fase 10 — el primer repositorio de <see cref="Draft"/>, deliberadamente mínimo: solo lo que
/// <see cref="Compose.StartReplyHandler"/> necesita. Fase 15 agrega las consultas de lectura para
/// la UI: <see cref="ListOpenByCustomerAsync"/> ("retomar autoguardado") y
/// <see cref="ListSentByThreadAsync"/> (thread unificado inbound+outbound).
/// </summary>
public interface IDraftRepository
{
    /// <summary>Busca por <c>(TenantId, Id)</c>. Devuelve <c>null</c> si no existe o pertenece a otro tenant.</summary>
    Task<Draft?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Busca un <see cref="Draft"/> reutilizable para el mismo reply: mismo <c>CustomerId</c>,
    /// <c>Status == Draft</c>, y <c>ReplyContext.IncomingEmailId == incomingEmailId</c> — evita
    /// duplicar drafts si el usuario reabre el mismo reply antes de enviarlo o descartarlo (plan
    /// §36 Fase 10, punto 5). Devuelve <c>null</c> si no hay ninguno.
    /// </summary>
    Task<Draft?> FindOpenReplyDraftAsync(
        Guid tenantId,
        Guid customerId,
        Guid incomingEmailId,
        CancellationToken ct = default
    );

    Task AddAsync(Draft entity, CancellationToken ct = default);

    /// <summary>
    /// Fase 15 — "retomar un autoguardado": drafts en <see cref="DraftStatus.Draft"/> (nunca
    /// <c>Sent</c>/<c>Discarded</c>/<c>Failed</c>) de un customer, más reciente primero
    /// (<c>UpdatedAtUtc DESC</c>). Usa <c>IX_Drafts_TenantId_CustomerId_Status_UpdatedAtUtc</c>
    /// (mismo índice que <see cref="FindOpenReplyDraftAsync"/>, ya creado en Fase 10).
    /// </summary>
    Task<PagedResult<Draft>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Fase 15 — drafts <see cref="DraftStatus.Sent"/> de un hilo puntual, para el thread
    /// unificado (<c>ListThreadMessagesHandler</c>). Sin paginar a propósito: un hilo real está
    /// acotado (<see cref="Domain.Inbox.EmailThread.MessageCount"/> lo trackea), el caller fusiona
    /// esta lista con los <c>IncomingEmail</c> del mismo hilo y pagina el resultado ya mezclado
    /// (nunca por fuente). Usa <c>IX_Drafts_TenantId_EmailThreadId_Status</c> — ver el WHY-comment
    /// de <see cref="Draft.EmailThreadId"/> sobre por qué esto no reusa el filtrado en memoria de
    /// <see cref="FindOpenReplyDraftAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Draft>> ListSentByThreadAsync(Guid tenantId, Guid emailThreadId, CancellationToken ct = default);

    /// <summary>
    /// Fase 16 — candidatos a <c>DraftCleanupJob</c> (plan §30): drafts <see cref="DraftStatus.Draft"/>
    /// (nunca enviados ni descartados) cuyo <c>UpdatedAtUtc</c> es anterior a
    /// <paramref name="updatedBeforeUtc"/>, más viejo primero, acotado a <paramref name="limit"/>
    /// filas por corrida (mismo criterio de batching que <c>ConnectorsRetentionScheduler</c>). A
    /// diferencia de <see cref="ListOpenByCustomerAsync"/>/<see cref="ListSentByThreadAsync"/> NO
    /// filtra por <c>TenantId</c> — el job corre global, cross-tenant, una sola pasada. Entidades
    /// devueltas con tracking habilitado (no <c>AsNoTracking</c>): el job llama
    /// <see cref="Draft.Discard"/> sobre cada una. Usa <c>IX_Drafts_Status_UpdatedAtUtc</c>.
    /// </summary>
    Task<IReadOnlyList<Draft>> ListAbandonedAsync(DateTime updatedBeforeUtc, int limit, CancellationToken ct = default);
}
