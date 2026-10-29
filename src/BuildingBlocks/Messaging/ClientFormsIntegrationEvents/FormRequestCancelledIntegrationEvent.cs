namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_request_cancelled.v1</summary>
public sealed record FormRequestCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public required string Title { get; init; }
    public required string Reason { get; init; }
    public Guid? CancelledByUserId { get; init; }
    public required bool NotifyClient { get; init; }
}
