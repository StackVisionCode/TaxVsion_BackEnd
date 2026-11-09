using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

[MessageIdentity("BuildingBlocks.Messaging.ClientFormsIntegrationEvents.ClientFormNotificationSettingsSyncRequestedIntegrationEvent")]
public sealed record ClientFormNotificationSettingsSyncRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid CommandId { get; init; }
    public required bool PushNotificationsEnabled { get; init; }
}
