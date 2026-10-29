namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.customer_profile_update_from_form_requested.v1</summary>
public sealed record CustomerProfileUpdateFromFormRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid CommandId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string SourceService { get; init; }
    public required Guid SourceReferenceId { get; init; }
    public required int RevisionNumber { get; init; }
    public required int TargetCatalogVersion { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public required ProtectedPayload Payload { get; init; }
}
