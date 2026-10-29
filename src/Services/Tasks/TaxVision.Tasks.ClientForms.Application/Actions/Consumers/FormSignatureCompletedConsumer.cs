using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

/// <summary>P-18 — la firma se completó (todos firmaron): marca la revisión Completed.</summary>
public static class FormSignatureCompletedConsumer
{
    public static async Task Handle(
        SignatureRequestCompletedIntegrationEvent evt,
        IFormRequestRepository requests,
        FormActionPlanner planner,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequestCompletedIntegrationEvent> logger,
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
            if (request.MarkSignatureCompleted(revision).IsFailure)
                return;
            var outgoing = new DeferredMessages();
            await planner.PlanAfterSignatureAsync(request, revision, true, outgoing, evt.OccurredOn, ct);
            // Guardar antes de publicar (T-8).
            await unitOfWork.SaveChangesAsync(ct);
            await outgoing.PublishAsync(bus);
        }
    }
}
