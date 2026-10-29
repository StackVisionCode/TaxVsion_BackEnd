using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Consumers;

public static class CustomerCreationFromFormCompletedConsumer
{
    public static async Task Handle(
        CustomerCreationFromFormCompletedIntegrationEvent evt,
        IFormRequestRepository requests,
        IFormParticipantRepository participants,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CustomerCreationFromFormCompletedIntegrationEvent> logger,
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
            if (request is null || !Complete(request, evt))
                return;
            var outgoing = new DeferredMessages();
            await FormCustomerLinker.LinkCreatedCustomerAsync(
                request,
                evt.CustomerId,
                requests,
                participants,
                audit,
                outgoing,
                correlation.CorrelationId,
                logger,
                evt.OccurredOn,
                ct
            );
            // Guardar antes de publicar (T-8).
            await unitOfWork.SaveChangesAsync(ct);
            await outgoing.PublishAsync(bus);
            metrics.RecordActionRun(FormActionNames.CreateCustomer, FormActionRunStatus.Completed);
        }
    }

    // Un Completed repetido, o de un comando que no es nuestro, no cambia nada.
    private static bool Complete(FormRequest request, CustomerCreationFromFormCompletedIntegrationEvent evt)
    {
        var completed = request.CompleteAction(
            evt.RequestId,
            FormActionNames.CustomerResult,
            evt.CustomerId,
            evt.Warnings.Count == 0 ? null : string.Join(',', evt.Warnings),
            evt.OccurredOn
        );
        return completed.IsSuccess && completed.Value == FormActionRunTransition.Applied;
    }
}
