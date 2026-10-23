using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

public static class CustomerCreationFromFormRejectedConsumer
{
    public static async Task Handle(
        CustomerCreationFromFormRejectedIntegrationEvent evt,
        IFormRequestRepository requests,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CustomerCreationFromFormRejectedIntegrationEvent> logger,
        IClientFormsMetrics metrics,
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
            if (request is null || !Reject(request, evt))
                return;
            await bus.PublishAsync(
                CustomerCreationCommandFactory.Failed(
                    request,
                    FormActionNames.CreateCustomer,
                    evt.ErrorCode,
                    correlation.CorrelationId
                )
            );
            await unitOfWork.SaveChangesAsync(ct);
            metrics.RecordActionRun(FormActionNames.CreateCustomer, FormActionRunStatus.Rejected);
        }
    }

    // Solo un run Requested pasa a Rejected: un Completed previo del mismo comando gana.
    private static bool Reject(FormRequest request, CustomerCreationFromFormRejectedIntegrationEvent evt)
    {
        var rejected = request.RejectAction(
            evt.RequestId,
            evt.ErrorCode,
            evt.ExistingCustomerId,
            evt.MatchedBy,
            evt.OccurredOn
        );
        return rejected.IsSuccess && rejected.Value == FormActionRunTransition.Applied;
    }
}
