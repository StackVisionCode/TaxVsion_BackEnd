using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Tenants;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class TenantRegistry(AuthDbContext db) : ITenantRegistry
{
    public Task<bool> ExistsActiveAsync(Guid tenantId, CancellationToken ct = default)
        => db.Tenants.AnyAsync(tenant => tenant.Id == tenantId && tenant.IsActive, ct);

    public async Task UpsertCreatedAsync(
        Guid tenantId,
        string name,
        string subDomain,
        CancellationToken ct = default)
    {
        var existing = await db.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == tenantId, ct);
        if (existing is not null)
        {
            existing.UpdateFromCreatedEvent(name, subDomain);
            return;
        }

        var result = Tenant.Register(tenantId, name, subDomain);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error.Message);
        }

        await db.Tenants.AddAsync(result.Value, ct);
    }
}
