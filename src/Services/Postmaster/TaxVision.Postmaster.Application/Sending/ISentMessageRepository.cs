using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending;

public interface ISentMessageRepository
{
    Task AddAsync(SentMessage message, CancellationToken ct = default);

    /// <summary>
    /// Marca el <see cref="SentMessage"/> para borrado — usado solo en el rollback de un intento que
    /// falló ANTES de salir al proveedor (ej. no se pudo bajar el adjunto). El borrado real ocurre en
    /// el <c>SaveChanges</c> del handler, junto con la liberación de la reserva de idempotencia.
    /// </summary>
    void Remove(SentMessage message);

    /// <summary>Con destinatarios, sin seguimiento y sin filtro de tenant: lo lee el paso de envío.</summary>
    Task<SentMessage?> GetForDispatchAsync(Guid id, CancellationToken ct = default);

    /// <summary>El mensaje de esa clave de idempotencia, si existe (sin seguimiento).</summary>
    Task<SentMessage?> FindByIdempotencyKeyAsync(Guid tenantId, string idempotencyKey, CancellationToken ct = default);

    /// <summary>Incluye Events — usado por el timeline de auditoría (Fase 6).</summary>
    Task<Result<SentMessage>> GetByIdWithEventsAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>"Email activity": página del tenant, más recientes primero, con destinatarios.</summary>
    Task<(IReadOnlyList<SentMessage> Items, int Total)> ListForActivityAsync(
        Guid tenantId,
        SentMessageActivityFilter filter,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>Con destinatarios y eventos, del tenant indicado.</summary>
    Task<Result<SentMessage>> GetDetailAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Con destinatarios y sin filtro de tenant: los avisos del proveedor llegan sin oficina.</summary>
    Task<Result<SentMessage>> GetByIdForFeedbackAsync(Guid id, CancellationToken ct = default);

    /// <summary>Busca por el Message-ID con el que salió (lo citan los avisos de no entrega).</summary>
    Task<Result<SentMessage>> FindByInternetMessageIdForFeedbackAsync(
        string internetMessageId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Destinatarios del proveedor del sistema que siguen sin resultado final (Accepted o Deferred) y
    /// salieron dentro de la ventana. Los más viejos primero.
    /// </summary>
    Task<IReadOnlyList<PendingDeliveryRef>> ListPendingDeliveryAsync(
        DateTime sentFromUtc,
        DateTime sentToUtc,
        int max,
        CancellationToken ct = default
    );

    /// <summary>
    /// De los ids del proveedor recibidos, los que son de correos nuestros salidos por el proveedor del
    /// sistema. Una consulta por lote: los avisos de otros entornos que comparten la cuenta se descartan.
    /// </summary>
    Task<IReadOnlySet<string>> FindKnownProviderMessageIdsAsync(
        IReadOnlyCollection<string> providerMessageIds,
        CancellationToken ct = default
    );

    /// <summary>Busca por el id propio del proveedor y el destinatario, cuando el aviso no trae nuestro Message-ID.</summary>
    Task<Result<SentMessage>> FindByProviderMessageIdForFeedbackAsync(
        string providerMessageId,
        string recipientAddress,
        CancellationToken ct = default
    );
}
