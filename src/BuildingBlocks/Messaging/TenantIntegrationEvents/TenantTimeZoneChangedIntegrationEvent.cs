using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>
/// P9: publicado por Tenant solo cuando cambia la zona de la oficina, con la anterior y la nueva. Es aditivo:
/// <see cref="TenantUpdatedIntegrationEvent"/> sigue llevando el estado completo para las proyecciones; este es para
/// quien necesita re-anclar (Calendar registra el cambio, Tasks recalcula vencimientos, Communication avisa).
/// </summary>
[MessageIdentity("tenant.time_zone_changed.v1")]
public sealed record TenantTimeZoneChangedIntegrationEvent : IntegrationEvent
{
    public required Guid ChangedTenantId { get; init; }
    public required string PreviousTimeZoneId { get; init; }
    public required string NewTimeZoneId { get; init; }
    public required DateTime ChangedAtUtc { get; init; }

    /// <summary>Versión de la oficina al emitirlo; ordena frente a <see cref="TenantUpdatedIntegrationEvent"/>.</summary>
    public required long TenantVersion { get; init; }
}
