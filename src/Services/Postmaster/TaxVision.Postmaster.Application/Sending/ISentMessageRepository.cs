using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending;

public interface ISentMessageRepository
{
    Task AddAsync(SentMessage message, CancellationToken ct = default);

    /// <summary>Incluye Events — usado por el timeline de auditoría (Fase 6).</summary>
    Task<Result<SentMessage>> GetByIdWithEventsAsync(Guid tenantId, Guid id, CancellationToken ct = default);
}
