using TaxVision.Notes.Domain.Backfill;

namespace TaxVision.Notes.Application.Backfill.Abstractions;

/// <summary>Marca de "backfill de CustomerDirectoryEntry ya corrido" por tenant (Fase 4B).</summary>
public interface ITenantBackfillStateRepository
{
    Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantBackfillState entity, CancellationToken ct = default);
}
