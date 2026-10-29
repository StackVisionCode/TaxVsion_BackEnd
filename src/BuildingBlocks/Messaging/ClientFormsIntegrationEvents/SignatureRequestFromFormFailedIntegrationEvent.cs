using BuildingBlocks.Messaging.SignatureIntegrationEvents;

namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>signature.signature_request_from_form_failed.v1 — hecho a taxvision-events.</summary>
public sealed record SignatureRequestFromFormFailedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required string ErrorCode { get; init; }
    public SignatureExternalReference? ExternalReference { get; init; }
}
