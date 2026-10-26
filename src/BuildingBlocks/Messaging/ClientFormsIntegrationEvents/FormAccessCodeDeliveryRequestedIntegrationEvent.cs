namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>task.form_access_code_delivery_requested.v1</summary>
public sealed record FormAccessCodeDeliveryRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid CommandId { get; init; }
    public Guid? FormRequestId { get; init; }
    public Guid? IntakeLinkId { get; init; }
    public required Guid ChallengeId { get; init; }
    public ClientFormParticipant? Participant { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public required ProtectedPayload Payload { get; init; }
}

public sealed record FormAccessCodeDeliveryPayload(string Email, string Code, string Title, string Language = "En");
