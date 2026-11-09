using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>signature.signature_request_from_form_failed.v1 — hecho a taxvision-events.</summary>
[MessageIdentity("BuildingBlocks.Messaging.ClientFormsIntegrationEvents.SignatureRequestFromFormFailedIntegrationEvent")]
public sealed record SignatureRequestFromFormFailedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required string ErrorCode { get; init; }
    public SignatureExternalReference? ExternalReference { get; init; }
}
