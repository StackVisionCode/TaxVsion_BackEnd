using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.RateLimiting.Abstractions;
using TaxVision.Tasks.Domain.RateLimiting;

namespace TaxVision.Tasks.Infrastructure.Persistence.Repositories;

// El consumer corre en un scope de Wolverine sin TenantContext ambiente, así que el filtro global
// devolvería 0 filas: IgnoreQueryFilters() explícito y el tenantId sale del propio evento.
public sealed class TenantPlanCodeProjectionRepository(TasksDbContext db) : ITenantPlanCodeProjectionRepository
{
    public async Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);
}
