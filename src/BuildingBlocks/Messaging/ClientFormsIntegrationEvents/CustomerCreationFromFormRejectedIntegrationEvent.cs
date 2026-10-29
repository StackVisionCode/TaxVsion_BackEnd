namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>customer.customer_creation_from_form_rejected.v1</summary>
public sealed record CustomerCreationFromFormRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required Guid SourceReferenceId { get; init; }
    public required string ErrorCode { get; init; }
    public Guid? ExistingCustomerId { get; init; }
    public string? MatchedBy { get; init; }
}
