using BuildingBlocks.Domain;

namespace TaxVision.Tenant.Domain.Events;

/// <summary>
/// Cambió el nombre o la zona horaria. Un solo evento con el estado completo, aunque cambien los dos: el aviso
/// de integración es uno por edición.
/// </summary>
public sealed record TenantUpdatedDomainEvent(
    Guid TenantId,
    string Name,
    string DefaultTimeZoneId,
    DateTime UpdatedAtUtc
) : IDomainEvent;
