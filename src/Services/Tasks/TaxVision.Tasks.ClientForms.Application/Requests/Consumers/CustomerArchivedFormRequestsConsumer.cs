using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Consumers;

public static class CustomerArchivedFormRequestsConsumer
{
    public static async Task Handle(
        CustomerArchivedIntegrationEvent evt,
        IFormRequestRepository requests,
        IFormAccessGrantRepository grants,
        IFormAccessSessionRepository sessions,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var openRequests = await requests.ListOpenByCustomerAsync(evt.TenantId, evt.CustomerId, ct);
            var outgoing = new DeferredMessages();
            foreach (var request in openRequests)
            {
                if (request.CancelBecauseCustomerArchived(evt.CustomerId, evt.OccurredOn).IsFailure)
                    continue;
                await FormExternalAccessRevoker.RevokeAsync(request, evt.OccurredOn, grants, sessions, ct);
                audit.Record(
                    evt.TenantId,
                    request.FormTemplateId,
                    request.Id,
                    "request.cancelled",
                    null,
                    "{\"reason\":\"customer_archived\"}"
                );
                outgoing.Add(
                    FormRequestIntegrationEventFactory.Cancelled(request, null, false, correlation.CorrelationId)
                );
            }
            // Guardar antes de publicar (T-8).
            await unitOfWork.SaveChangesAsync(ct);
            await outgoing.PublishAsync(bus);
        }
    }
}
