using BuildingBlocks.Tenancy;
using TaxVision.PaymentClient.Domain.Tenants;

namespace TaxVision.PaymentClient.Application.Abstractions;

public interface ITenantRegistry
{
    Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);

    Task UpsertCreatedAsync(
        Guid tenantId,
        string name,
        string subDomain,
        TenantKind kind,
        string defaultTimeZoneId,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    Task UpdateStatusAsync(
        Guid tenantId,
        string status,
        bool isActive,
        DateTime nowUtc,
        CancellationToken ct = default
    );
}
