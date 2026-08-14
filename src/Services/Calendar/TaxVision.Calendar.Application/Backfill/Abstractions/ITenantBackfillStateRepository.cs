using TaxVision.Calendar.Domain.Backfill;

namespace TaxVision.Calendar.Application.Backfill.Abstractions;

public interface ITenantBackfillStateRepository
{
    Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantBackfillState entity, CancellationToken ct = default);
}
