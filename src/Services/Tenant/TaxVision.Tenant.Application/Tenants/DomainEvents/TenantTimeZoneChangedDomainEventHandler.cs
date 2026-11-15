using BuildingBlocks.Messaging.TenantIntegrationEvents;
using TaxVision.Tenant.Domain.Events;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.DomainEvents;

/// <summary>P9: el cambio de zona sale como evento propio, con la zona anterior.</summary>
public static class TenantTimeZoneChangedDomainEventHandler
{
    public static TenantTimeZoneChangedIntegrationEvent Handle(
        TenantTimeZoneChangedDomainEvent changed,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = changed.TenantId,
            ChangedTenantId = changed.TenantId,
            PreviousTimeZoneId = changed.PreviousTimeZoneId,
            NewTimeZoneId = changed.NewTimeZoneId,
            ChangedAtUtc = changed.ChangedAtUtc,
            TenantVersion = changed.Version,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
        };
}
