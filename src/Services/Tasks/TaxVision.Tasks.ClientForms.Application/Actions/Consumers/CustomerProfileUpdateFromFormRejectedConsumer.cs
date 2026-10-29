using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

// P-13.2 — Hecho de Customer: nada se aplicó. La request pasa a Rejected con los motivos.
public static class CustomerProfileUpdateFromFormRejectedConsumer
{
    public static async Task Handle(
        CustomerProfileUpdateFromFormRejectedIntegrationEvent evt,
        IFormRequestRepository requests,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerProfileUpdateFromFormRejectedIntegrationEvent> logger,
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

            var failures = new List<FormProfileUpdateOutcome>();
            foreach (var failure in evt.Failures)
                failures.Add(new FormProfileUpdateOutcome(failure.Target, false, failure.Code));

            var marked = request.MarkProfileUpdateRejected(evt.CommandId, failures, evt.OccurredOn);
            if (marked.IsFailure)
                return;
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
