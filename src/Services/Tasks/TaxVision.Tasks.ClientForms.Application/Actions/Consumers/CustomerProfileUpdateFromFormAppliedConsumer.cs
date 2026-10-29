using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

// P-13.2 — Hecho de Customer: aplicados + fallidos. Con fallos => PartiallyApplied; sin fallos => Applied.
public static class CustomerProfileUpdateFromFormAppliedConsumer
{
    public static async Task Handle(
        CustomerProfileUpdateFromFormAppliedIntegrationEvent evt,
        IFormRequestRepository requests,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerProfileUpdateFromFormAppliedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(FormActionResultSupport.CorrelationOf(evt.CorrelationId, evt.EventId)))
        {
            var request = await FormActionResultSupport.LoadAsync(
                evt.TenantId,
                evt.SourceReferenceId,
                requests,
                logger,
                ct
            );
            if (request is null)
                return;

            var outcomes = new List<FormProfileUpdateOutcome>();
            foreach (var target in evt.AppliedTargets)
                outcomes.Add(new FormProfileUpdateOutcome(target, true, null));
            foreach (var failure in evt.Failures)
                outcomes.Add(new FormProfileUpdateOutcome(failure.Target, false, failure.Code));

            var marked = request.MarkProfileUpdateApplied(evt.CommandId, outcomes, evt.OccurredOn);
            if (marked.IsFailure)
                return;
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
