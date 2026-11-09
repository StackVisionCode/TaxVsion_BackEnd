using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>
/// Publicado por Tenant al renombrar la oficina o cambiar su zona horaria. Lleva el estado completo
/// de ambos campos (no el delta): cada proyección los copia tal cual. <see cref="UpdatedAtUtc"/> ordena:
/// un consumidor ignora un evento más viejo que lo que ya tiene.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.TenantIntegrationEvents.TenantUpdatedIntegrationEvent")]
public sealed record TenantUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid UpdatedTenantId { get; init; }
    public required string Name { get; init; }
    public required string DefaultTimeZoneId { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }

    /// <summary>Versión de la oficina al emitirlo. Null en mensajes viejos: ordena <see cref="UpdatedAtUtc"/>.</summary>
    public long? TenantVersion { get; init; }
}
