namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_request_returned.v1</summary>
public sealed record FormRequestReturnedIntegrationEvent : IntegrationEvent
{
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public required string Title { get; init; }
    public required int RevisionNumber { get; init; }
    public required int FieldCommentCount { get; init; }
    public required Guid ReturnedByUserId { get; init; }
    public DateTime? DueAtUtc { get; init; }
}
