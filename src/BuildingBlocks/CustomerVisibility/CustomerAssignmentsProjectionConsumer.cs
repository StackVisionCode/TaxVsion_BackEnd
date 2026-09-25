using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;

namespace BuildingBlocks.CustomerVisibility;

// Consumer COMPARTIDO del snapshot CustomerAssignmentsChanged: mantiene la proyección local (version-guarded,
// idempotente, event-carried state transfer). Cada servicio lo engancha en Wolverine con
// opts.Discovery.IncludeType(typeof(CustomerAssignmentsProjectionConsumer)) (vive fuera de su .Application).
public static class CustomerAssignmentsProjectionConsumer
{
    public static async Task Handle(
        CustomerAssignmentsChangedIntegrationEvent evt,
        ICustomerAssignmentProjectionStore store,
        IUnitOfWork unitOfWork,
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
            var applied = await store.GetVersionAsync(evt.TenantId, evt.CustomerId, ct);
            if (applied is { } current && evt.Version <= current)
                return; // snapshot igual o más nuevo ya aplicado → ignorar (reordenado/duplicado)

            await store.ReplaceAsync(evt.TenantId, evt.CustomerId, evt.AssigneeUserIds, evt.Version, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
