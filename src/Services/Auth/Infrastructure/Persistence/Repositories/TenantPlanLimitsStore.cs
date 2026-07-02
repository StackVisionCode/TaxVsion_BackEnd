using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

/// <summary>Implementación EF Core del almacén de límites del plan del tenant.</summary>
public sealed class TenantPlanLimitsStore(AuthDbContext db) : ITenantPlanLimitsStore
{
    public Task<TenantPlanLimits?> GetAsync(Guid tenantId, CancellationToken ct = default)
        => db.TenantPlanLimits.FirstOrDefaultAsync(limits => limits.Id == tenantId, ct);

    public async Task AddAsync(TenantPlanLimits limits, CancellationToken ct = default)
        => await db.TenantPlanLimits.AddAsync(limits, ct);
}
