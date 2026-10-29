namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_action_failed.v1</summary>
public sealed record FormActionFailedIntegrationEvent : IntegrationEvent
{
    public required Guid FormRequestId { get; init; }
    public required string ActionType { get; init; }
    public required string ErrorCode { get; init; }
    public required Guid ResponsibleUserId { get; init; }
}
