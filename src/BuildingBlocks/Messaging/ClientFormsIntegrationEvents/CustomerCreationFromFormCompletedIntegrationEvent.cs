namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>customer.customer_creation_from_form_completed.v1</summary>
public sealed record CustomerCreationFromFormCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required Guid SourceReferenceId { get; init; }
    public required Guid CustomerId { get; init; }
    public required bool Replayed { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
