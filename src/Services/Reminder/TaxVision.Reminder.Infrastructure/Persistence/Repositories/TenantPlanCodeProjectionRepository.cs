using Microsoft.EntityFrameworkCore;
using TaxVision.Reminder.Application.RateLimiting.Abstractions;
using TaxVision.Reminder.Domain.RateLimiting;

namespace TaxVision.Reminder.Infrastructure.Persistence.Repositories;

// Consumer Wolverine sin TenantContext ambiente (no hay request HTTP), mismo criterio que
// UserPermissionsProjectionRepository: IgnoreQueryFilters() explícito, el tenantId ya viene
// confiable desde el evento.
public sealed class TenantPlanCodeProjectionRepository(ReminderDbContext db) : ITenantPlanCodeProjectionRepository
{
    public async Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) =>
        await db.TenantPlanCodeProjections.AddAsync(projection, ct);
}
