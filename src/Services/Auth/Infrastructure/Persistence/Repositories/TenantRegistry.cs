using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Tenants;
using BuildingBlocks.Tenancy;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class TenantRegistry(AuthDbContext db) : ITenantRegistry
{
    public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default)
        => db.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == tenantId, ct);

    public async Task UpsertCreatedAsync(
        Guid tenantId,
        string name,
        string subDomain,
        TenantKind kind,
        string defaultTimeZoneId,
        CancellationToken ct = default)
    {
        var existing = await db.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == tenantId, ct);
        if (existing is not null)
        {
            existing.UpdateFromCreatedEvent(
                name,
                subDomain,
                kind,
                defaultTimeZoneId);
            return;
        }

        var result = Tenant.Register(
            tenantId,
            name,
            subDomain,
            kind,
            defaultTimeZoneId);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error.Message);
        }

        await db.Tenants.AddAsync(result.Value, ct);
    }

    public async Task SetActiveAsync(
        Guid tenantId,
        bool isActive,
        CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(value => value.Id == tenantId, ct);
        if (tenant is not null)
            tenant.SetActive(isActive);
    }
}
