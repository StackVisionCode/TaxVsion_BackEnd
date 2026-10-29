namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>customer.customer_profile_update_from_form_applied.v1</summary>
public sealed record CustomerProfileUpdateFromFormAppliedIntegrationEvent : IntegrationEvent
{
    public required Guid CommandId { get; init; }
    public required Guid SourceReferenceId { get; init; }
    public IReadOnlyList<string> AppliedTargets { get; init; } = [];

    // Vacío = todo aplicado; con elementos = parcialmente aplicado.
    public IReadOnlyList<ClientFormProfileUpdateFailure> Failures { get; init; } = [];
}
