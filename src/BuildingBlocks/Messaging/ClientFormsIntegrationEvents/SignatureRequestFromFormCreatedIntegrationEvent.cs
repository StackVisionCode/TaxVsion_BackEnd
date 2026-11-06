using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>signature.signature_request_from_form_created.v1 — hecho a taxvision-events.</summary>
[MessageIdentity("BuildingBlocks.Messaging.ClientFormsIntegrationEvents.SignatureRequestFromFormCreatedIntegrationEvent")]
public sealed record SignatureRequestFromFormCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid RequestId { get; init; }
    public required Guid SignatureRequestId { get; init; }
    public SignatureExternalReference? ExternalReference { get; init; }
}
