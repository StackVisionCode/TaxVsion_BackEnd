using BuildingBlocks.Results;
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

    /// <summary>Incluye Events — usado por el timeline de auditoría (Fase 6).</summary>
    Task<Result<SentMessage>> GetByIdWithEventsAsync(Guid tenantId, Guid id, CancellationToken ct = default);
}
