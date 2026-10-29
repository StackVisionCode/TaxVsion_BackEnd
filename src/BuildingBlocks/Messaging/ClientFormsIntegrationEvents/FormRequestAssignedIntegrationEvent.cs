namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_request_assigned.v1</summary>
public sealed record FormRequestAssignedIntegrationEvent : IntegrationEvent
{
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public required string Title { get; init; }
    public required bool HasMessage { get; init; }
    public DateTime? DueAtUtc { get; init; }
    public int? TaxYear { get; init; }
    public required Guid RequestedByUserId { get; init; }
    public required Guid FormTemplateId { get; init; }
    public required int VersionNumber { get; init; }
}
