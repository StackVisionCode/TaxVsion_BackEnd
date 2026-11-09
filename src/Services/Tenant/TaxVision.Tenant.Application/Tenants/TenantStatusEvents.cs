using BuildingBlocks.Messaging;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Tenants;

/// <summary>Arma <see cref="TenantStatusChangedIntegrationEvent"/> con el estado actual del tenant.</summary>
public static class TenantStatusEvents
{
    public static TenantStatusChangedIntegrationEvent From(
        TaxVision.Tenant.Domain.Tenant tenant,
        string correlationId
    ) =>
        new()
        {
            ChangedTenantId = tenant.Id,
            TenantId = tenant.Id,
            Status = tenant.Status.ToString(),
            IsActive = tenant.Status == EnumTenantStatus.TenantStatus.Active,
            TenantVersion = tenant.Version,
            CorrelationId = correlationId,
        };
}
