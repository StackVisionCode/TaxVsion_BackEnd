using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

/// <summary>P-18 — documento sellado: guarda SealedFileId, hash posterior y certificado en la revisión.</summary>
public static class FormSignatureDocumentSealedConsumer
{
    public static async Task Handle(
        SignatureDocumentSealedIntegrationEvent evt,
        IFormRequestRepository requests,
        FormActionPlanner planner,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureDocumentSealedIntegrationEvent> logger,
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
            if (
                request
                    .MarkSignatureSealed(revision, evt.SealedFileId, evt.DocumentHashPost, evt.CertificateFileId)
                    .IsFailure
            )
                return;
            // Sellado implica completada: el alta que esperaba la firma corre aquí si no corrió antes.
            var outgoing = new DeferredMessages();
            await planner.PlanAfterSignatureAsync(request, revision, true, outgoing, evt.OccurredOn, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await outgoing.PublishAsync(bus);
        }
    }
}
