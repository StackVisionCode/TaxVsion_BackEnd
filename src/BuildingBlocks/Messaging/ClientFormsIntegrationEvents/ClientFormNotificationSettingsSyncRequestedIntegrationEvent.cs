namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

public sealed record ClientFormNotificationSettingsSyncRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid CommandId { get; init; }
    public required bool PushNotificationsEnabled { get; init; }
}
