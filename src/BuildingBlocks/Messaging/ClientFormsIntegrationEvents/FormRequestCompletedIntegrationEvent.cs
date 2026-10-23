namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_request_completed.v1</summary>
public sealed record FormRequestCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public required string Title { get; init; }
    public required int RevisionNumber { get; init; }
    public required Guid CompletedByUserId { get; init; }
}
