using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

/// <summary>P-18 — un firmante firmó: marca Signed (o Completed si ya firmaron todos).</summary>
public static class FormSignatureDocumentSignedConsumer
{
    public static async Task Handle(
        DocumentSignedIntegrationEvent evt,
        IFormRequestRepository requests,
        FormActionPlanner planner,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<DocumentSignedIntegrationEvent> logger,
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
            var transition = evt.IsRequestCompleted
                ? request.MarkSignatureCompleted(revision)
                : request.MarkSignatureSigned(revision);
            if (transition.IsFailure)
                return;
            var outgoing = new DeferredMessages();
            if (evt.IsRequestCompleted)
                await planner.PlanAfterSignatureAsync(request, revision, true, outgoing, evt.OccurredOn, ct);
            // Guardar antes de publicar (T-8).
            await unitOfWork.SaveChangesAsync(ct);
            await outgoing.PublishAsync(bus);
        }
    }
}
