using BuildingBlocks.Messaging.TenantIntegrationEvents;
using TaxVision.Tenant.Domain.Events;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.DomainEvents;

/// <summary>Renombre o cambio de zona horaria: devuelve el estado completo para las proyecciones.</summary>
public static class TenantUpdatedDomainEventHandler
{
    public static TenantUpdatedIntegrationEvent Handle(TenantUpdatedDomainEvent updated, Envelope envelope) =>
        new()
        {
            TenantId = updated.TenantId,
            UpdatedTenantId = updated.TenantId,
            Name = updated.Name,
            DefaultTimeZoneId = updated.DefaultTimeZoneId,
            UpdatedAtUtc = updated.UpdatedAtUtc,
            TenantVersion = updated.Version,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
        };
}
