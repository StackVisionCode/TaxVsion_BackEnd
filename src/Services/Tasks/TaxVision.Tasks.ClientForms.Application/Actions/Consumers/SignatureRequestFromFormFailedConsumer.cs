using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

/// <summary>P-18 — Signature no pudo crear la firma: marca la revisión Failed y avisa al responsable.</summary>
public static class SignatureRequestFromFormFailedConsumer
{
    public static async Task Handle(
        SignatureRequestFromFormFailedIntegrationEvent evt,
        IFormRequestRepository requests,
        FormActionPlanner planner,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequestFromFormFailedIntegrationEvent> logger,
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
            if (request.MarkSignatureFailed(revision).IsFailure)
                return;
            var outgoing = new DeferredMessages();
            await planner.PlanAfterSignatureAsync(request, revision, false, outgoing, evt.OccurredOn, ct);
            outgoing.Add(FormSignatureResultSupport.BuildNotice(request, evt.ErrorCode, correlation.CorrelationId));
            // Guardar antes de publicar (T-8).
            await unitOfWork.SaveChangesAsync(ct);
            await outgoing.PublishAsync(bus);
        }
    }
}
