using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.Tenants;

namespace TaxVision.PaymentApp.Tests.TestDoubles;

/// <summary>Proyección de tenants falsa: solo el nombre, que es lo único que el recibo necesita.</summary>
public sealed class FakeTenantRegistry(string officeName) : ITenantRegistry
{
    public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<Tenant?>(
            Tenant.Register(tenantId, officeName, "acme", TenantKind.Customer, "UTC", DateTime.UtcNow).Value
        );

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
