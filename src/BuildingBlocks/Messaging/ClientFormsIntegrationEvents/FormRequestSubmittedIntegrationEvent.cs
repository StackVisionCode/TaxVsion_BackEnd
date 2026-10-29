namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_request_submitted.v1</summary>
public sealed record FormRequestSubmittedIntegrationEvent : IntegrationEvent
{
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public Guid? ResponsibleUserId { get; init; }
    public string? CustomerDisplayName { get; init; }
    public required string Title { get; init; }
    public required int RevisionNumber { get; init; }
    public required bool IsResubmission { get; init; }
    public required Guid RequestedByUserId { get; init; }
    public required ClientFormActorRef SubmittedBy { get; init; }
}
