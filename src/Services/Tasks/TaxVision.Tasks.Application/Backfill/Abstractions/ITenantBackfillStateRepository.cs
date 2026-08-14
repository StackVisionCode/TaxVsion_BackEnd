using TaxVision.Tasks.Domain.Backfill;

namespace TaxVision.Tasks.Application.Backfill.Abstractions;

public interface ITenantBackfillStateRepository
{
    Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantBackfillState entity, CancellationToken ct = default);
}
