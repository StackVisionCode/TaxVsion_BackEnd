namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_access_link_delivery_requested.v1</summary>
public sealed record FormAccessLinkDeliveryRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid CommandId { get; init; }
    public required Guid FormRequestId { get; init; }
    public required ClientFormParticipant Participant { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public required ProtectedPayload Payload { get; init; }
}

public sealed record FormAccessLinkDeliveryPayload(
    string Email,
    string? Name,
    string AccessToken,
    string Title
);
