namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>customer.customer_profile_update_from_form_rejected.v1</summary>
public sealed record CustomerProfileUpdateFromFormRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid CommandId { get; init; }
    public required Guid SourceReferenceId { get; init; }
    public IReadOnlyList<ClientFormProfileUpdateFailure> Failures { get; init; } = [];
}
