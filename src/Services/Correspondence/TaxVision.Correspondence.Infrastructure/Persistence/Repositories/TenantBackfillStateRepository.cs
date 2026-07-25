using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Backfill;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

public sealed class TenantBackfillStateRepository(CorrespondenceDbContext db) : ITenantBackfillStateRepository
{
    public Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantBackfillStates.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);

    public async Task AddAsync(TenantBackfillState entity, CancellationToken ct = default)
    {
        await db.TenantBackfillStates.AddAsync(entity, ct);
    }

    // IgnoreQueryFilters: job cross-tenant (RBAC Fase 5) — CustomerEmailReconciliationJob necesita
    // la lista de TODOS los tenants backfilleados para iterarlos, nunca sirve una request autenticada.
    public async Task<IReadOnlyList<Guid>> ListAllTenantIdsAsync(CancellationToken ct = default) =>
        await db.TenantBackfillStates.IgnoreQueryFilters().AsNoTracking().Select(x => x.TenantId).ToListAsync(ct);
}
