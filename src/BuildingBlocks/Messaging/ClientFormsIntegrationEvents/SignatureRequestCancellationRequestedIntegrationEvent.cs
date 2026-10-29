using BuildingBlocks.Messaging.SignatureIntegrationEvents;

namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

/// <summary>
/// task.signature_request_cancellation_requested.v1 — comando punto a punto a la cola
/// signature-client-form-commands. Tasks lo envía cuando una revisión nueva supera a una firma
/// pendiente. Idempotente: si la solicitud ya está en estado terminal, Signature lo ignora.
/// </summary>
public sealed record SignatureRequestCancellationRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid RequestedByUserId { get; init; }
    public string? Reason { get; init; }
    public SignatureExternalReference? ExternalReference { get; init; }
}
