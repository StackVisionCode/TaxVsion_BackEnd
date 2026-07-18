using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.Tenants;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

public sealed class TenantRegistry(PaymentAppDbContext db) : ITenantRegistry
{
    public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == tenantId, ct);

    public async Task UpsertCreatedAsync(
        Guid tenantId,
        string name,
        string subDomain,
        TenantKind kind,
        string defaultTimeZoneId,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        var existing = await db.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == tenantId, ct);
        if (existing is not null)
            return;

        var result = Tenant.Register(tenantId, name, subDomain, kind, defaultTimeZoneId, nowUtc);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Message);

        await db.Tenants.AddAsync(result.Value, ct);
    }

    public async Task UpdateStatusAsync(
        Guid tenantId,
        string status,
        bool isActive,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(value => value.Id == tenantId, ct);
        tenant?.ApplyStatusChange(status, isActive, nowUtc);
    }
}
