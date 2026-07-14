namespace BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

/// <summary>
/// Publicado por Subscription cuando se libera la asignación vigente de un seat (release
/// manual, reasignación, o baja de usuario en Auth). Auth lo consume para revocar el
/// seat_id del JWT y bumpear PermissionsVersion.
/// </summary>
public sealed record SeatReleasedFromUserIntegrationEvent : IntegrationEvent
{
    public required Guid SeatId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid ReleasedByUserId { get; init; }
    public string? ReleaseReason { get; init; }
    public required DateTime ReleasedAtUtc { get; init; }
}
