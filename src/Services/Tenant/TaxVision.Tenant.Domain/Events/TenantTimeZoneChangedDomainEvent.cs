using BuildingBlocks.Domain;

namespace TaxVision.Tenant.Domain.Events;

/// <summary>P9: la oficina cambió de zona. Lleva la anterior para que los servicios puedan re-anclar.</summary>
public sealed record TenantTimeZoneChangedDomainEvent(
    Guid TenantId,
    string PreviousTimeZoneId,
    string NewTimeZoneId,
    DateTime ChangedAtUtc,
    long Version
) : IDomainEvent;
