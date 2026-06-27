using BuildingBlocks.Tenancy;

namespace TaxVision.Auth.Application.Abstractions;

public interface ITenantRegistry
{
    Task<Domain.Tenants.Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task UpsertCreatedAsync(
        Guid tenantId,
        string name,
        string subDomain,
        TenantKind kind,
        string defaultTimeZoneId,
        CancellationToken ct = default);
    Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default);
}
