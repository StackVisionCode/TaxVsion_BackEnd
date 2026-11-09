using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomerArchivedIntegrationEvent")]
public sealed record CustomerArchivedIntegrationEvent : IntegrationEvent
{
    public required Guid CustomerId { get; init; }
    public required Guid ArchivedByUserId { get; init; }
}
