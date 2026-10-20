using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.NotificationIntegrationEvents;

[MessageIdentity("notification.inapp.requested.v1")]
public sealed record InAppNotificationRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid RecipientUserId { get; init; }
    public required string Category { get; init; }
    public required string Kind { get; init; }
    public required string Priority { get; init; }
    public required string TitleKey { get; init; }
    public string? BodyKey { get; init; }
    public Dictionary<string, string> Params { get; init; } = new();
    public required string FallbackTitle { get; init; }
    public string? FallbackBody { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ActorDisplayName { get; init; }
    public required string ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public Dictionary<string, string> ResourceContext { get; init; } = new();
    public required string Action { get; init; }
    public required Guid SourceEventId { get; init; }
    public required string SourceEventType { get; init; }
    public string? GroupKey { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
}
