using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

/// <summary>P-18 — Signature creó la firma: guarda su Id en la revisión (estado Sent).</summary>
public static class SignatureRequestFromFormCreatedConsumer
{
    public static async Task Handle(
        SignatureRequestFromFormCreatedIntegrationEvent evt,
        IFormRequestRepository requests,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignatureRequestFromFormCreatedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        if (!FormSignatureResultSupport.TryResolve(evt.ExternalReference, out var requestId, out var revision))
            return;
        using (correlation.Push(FormActionResultSupport.CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            var request = await FormActionResultSupport.LoadAsync(evt.TenantId, requestId, requests, logger, ct);
            if (request is null)
                return;
            if (request.LinkSignatureRequest(revision, evt.SignatureRequestId).IsFailure)
                return;
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
