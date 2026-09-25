namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth al RETIRAR (offboard) a un usuario del tenant: estado terminal, no reversible.
/// A diferencia de <see cref="UserDeactivatedIntegrationEvent"/> (baja reversible), cada servicio que lo
/// consuma debe reasignar el trabajo activo del usuario al sucesor y conservar la data de auditoría/procedencia.
/// Evento NUEVO (no se tocan los contratos existentes): los servicios que no lo manejen simplemente lo ignoran.
/// </summary>
public sealed record UserOffboardedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string ActorType { get; init; }

    /// <summary>Quién ejecutó el retiro (auditoría).</summary>
    public Guid? OffboardedByUserId { get; init; }

    /// <summary>Sucesor al que reasignar el trabajo activo; null = rutar a "oficina" / sin sucesor.</summary>
    public Guid? SuccessorUserId { get; init; }

    public required DateTime RemovedAtUtc { get; init; }
}
