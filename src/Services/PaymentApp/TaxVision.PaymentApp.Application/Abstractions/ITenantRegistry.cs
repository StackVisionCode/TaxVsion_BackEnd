using BuildingBlocks.Tenancy;
using TaxVision.PaymentApp.Domain.Tenants;

namespace TaxVision.PaymentApp.Application.Abstractions;

/// <summary>Proyección local del tenant, alimentada por los consumers de
/// <c>TenantCreatedIntegrationEvent</c> / <c>TenantStatusChangedIntegrationEvent</c>. Fuente
/// de verdad para <c>TenantStatusGateMiddleware</c> — nunca se llama a Auth/Tenant en el hot
/// path de un cobro.</summary>
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
