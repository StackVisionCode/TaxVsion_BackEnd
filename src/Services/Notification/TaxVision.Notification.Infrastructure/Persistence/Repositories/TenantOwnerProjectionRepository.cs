using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Recipients;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

// IgnoreQueryFilters() explícito (G8): se usa desde consumers de Wolverine y el job de reconciliación,
// sin TenantContext ambiente — el filtro global fail-closed daría 0 filas. tenantId viene explícito.
public sealed class TenantOwnerProjectionRepository(NotificationDbContext db) : ITenantOwnerProjectionRepository
{
    public async Task<Guid?> GetActiveOwnerUserIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        var owners = await db
            .TenantOwnerProjections.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .OrderBy(p => p.RegisteredAtUtc)
            .ThenBy(p => p.UserId)
            .Select(p => (Guid?)p.UserId)
            .FirstOrDefaultAsync(ct);
        return owners;
    }

    public async Task<IReadOnlyList<Guid>> GetActiveAdminUserIdsAsync(Guid tenantId, CancellationToken ct = default) =>
        await db
            .TenantOwnerProjections.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .Select(p => p.UserId)
            .ToListAsync(ct);

    public Task<TenantOwnerProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        db
            .TenantOwnerProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public Task<TenantOwnerProjection?> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        GetAsync(tenantId, userId, ct);

    public async Task AddAsync(TenantOwnerProjection projection, CancellationToken ct = default) =>
        await db.TenantOwnerProjections.AddAsync(projection, ct);
}
