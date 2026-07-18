namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Publicado por Subscription cuando un seat queda asignado a un empleado del tenant.
/// Auth lo consume para incrustar seat_id/seat_status en el JWT y para bumpear
/// PermissionsVersion de sesiones vigentes.
/// </summary>
public sealed record SeatAssignedToUserIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid AssignedByUserId { get; init; }
    public required string SeatType { get; init; }
    public required DateTime AssignedAtUtc { get; init; }
    public DateTime? SeatExpiresAtUtc { get; init; }
}
