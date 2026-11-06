using BuildingBlocks.Messaging;
using TaxVision.Tenant.Domain.Events;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.DomainEvents;

/// <summary>El estado de la oficina cambió: devuelve el aviso de integración (cascada por el outbox).</summary>
public static class TenantStatusChangedDomainEventHandler
{
    public static TenantStatusChangedIntegrationEvent Handle(TenantStatusChangedDomainEvent changed, Envelope envelope) =>
        new()
        {
            ChangedTenantId = changed.TenantId,
            TenantId = changed.TenantId,
            Status = changed.Status.ToString(),
            IsActive = changed.IsActive,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
        };
}
