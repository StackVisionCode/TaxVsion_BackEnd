using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.Tenants;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

/// <summary>Proyección de tenants falsa: el nombre y, opcionalmente, el perfil de facturación del recibo.</summary>
public sealed class FakeTenantRegistry(string officeName, Action<Tenant>? shape = null) : ITenantRegistry
{
    public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = Tenant.Register(tenantId, officeName, "acme", TenantKind.Customer, "UTC", DateTime.UtcNow).Value;
        shape?.Invoke(tenant);
        return Task.FromResult<Tenant?>(tenant);
    }

    public Task UpsertCreatedAsync(
        Guid tenantId,
        string name,
        string subDomain,
        TenantKind kind,
        string defaultTimeZoneId,
        DateTime nowUtc,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task UpdateStatusAsync(
        Guid tenantId,
        string status,
        bool isActive,
        DateTime nowUtc,
        CancellationToken ct = default
    ) => throw new NotSupportedException();
}
