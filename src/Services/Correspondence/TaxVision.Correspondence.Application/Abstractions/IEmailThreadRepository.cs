using BuildingBlocks.Common;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Consultada por <see cref="Ingest.RawMessageReceivedConsumer"/> (Fase 4) para resolver el
/// <see cref="EmailThread"/> de un mensaje entrante (Layer 1: por <c>ProviderThreadId</c>; Layer
/// 2/3: por el <c>EmailThreadId</c> de un <see cref="IncomingEmail"/> relacionado, vía <see cref="GetByIdAsync"/>).
/// </summary>
public interface IEmailThreadRepository
{
    /// <summary>Busca por <c>(TenantId, ProviderThreadId)</c>. Devuelve <c>null</c> si no existe.</summary>
    Task<EmailThread?> FindByProviderThreadIdAsync(
        Guid tenantId,
        string providerThreadId,
        CancellationToken ct = default
    );

    Task<EmailThread?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task AddAsync(EmailThread entity, CancellationToken ct = default);

    /// <summary>
    /// Threads activos del mismo customer con actividad desde <paramref name="sinceUtc"/>, usado
    /// solo por el fallback opcional de subject-normalization (Layer 4, <see cref="Ingest.ThreadResolver"/>).
    /// Excluye threads archivados a propósito: un match ahí terminaría fallando en
    /// <c>EmailThread.AppendMessage</c>, que rechaza mensajes nuevos sobre un thread archivado.
    /// </summary>
    Task<IReadOnlyList<EmailThread>> FindRecentByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        DateTime sinceUtc,
        CancellationToken ct = default
    );

    /// <summary>
    /// Fase 9 — inbox del customer para el cliente final, más reciente primero (usa
    /// <c>IX_EmailThreads_TenantId_CustomerId_LastMessageAtUtc</c>). A diferencia de
    /// <see cref="FindRecentByCustomerAsync"/> (ventana de tiempo, solo threads activos, sin
    /// paginar — uso interno del thread-matching) esto es un listado paginado sin filtro de
    /// estado: el cliente final también quiere ver sus hilos archivados.
    /// </summary>
    Task<PagedResult<EmailThread>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int page,
        int size,
        CancellationToken ct = default
    );
}
