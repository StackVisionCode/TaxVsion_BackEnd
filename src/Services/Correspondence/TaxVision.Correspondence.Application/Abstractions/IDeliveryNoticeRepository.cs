using TaxVision.Correspondence.Domain.Delivery;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>Avisos de entrega de los correos enviados desde el CRM (ver <see cref="DeliveryNotice"/>).</summary>
public interface IDeliveryNoticeRepository
{
    /// <summary>
    /// Idempotencia del consumer: Postmaster publica un evento por cambio de resultado y su EventId es aleatorio, así
    /// que la clave es (envío, destinatario, resultado).
    /// </summary>
    Task<bool> ExistsAsync(
        Guid tenantId,
        Guid sentMessageId,
        string recipient,
        DeliveryOutcome outcome,
        CancellationToken ct = default
    );

    Task AddAsync(DeliveryNotice notice, CancellationToken ct = default);

    Task<DeliveryNotice?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Avisos de un hilo, más viejo primero (solo lectura). Acotado como los mensajes del hilo.</summary>
    Task<IReadOnlyList<DeliveryNotice>> ListByThreadAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    );

    /// <summary>Igual, TRACKED: para marcar leídos/no leídos en bloque.</summary>
    Task<IReadOnlyList<DeliveryNotice>> ListByThreadForUpdateAsync(
        Guid tenantId,
        Guid emailThreadId,
        CancellationToken ct = default
    );

    /// <summary>El aviso más reciente de cada hilo pedido (fila del Inbox). Un hilo sin avisos no aparece.</summary>
    Task<IReadOnlyDictionary<Guid, DeliveryNotice>> GetLatestByThreadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> emailThreadIds,
        CancellationToken ct = default
    );
}
