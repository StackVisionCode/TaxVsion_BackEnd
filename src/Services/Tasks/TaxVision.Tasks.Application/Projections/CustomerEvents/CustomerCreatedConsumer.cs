using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Backfill.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Application.Projections.CustomerEvents;

// Los siete consumers de este grupo mantienen CustomerDirectoryEntry. Todos arrancan llamando a
// EnsureBackfilledAsync: recibir cualquier evento de Customer es la única forma en que Task
// descubre que un tenant existe.

public static class CustomerCreatedConsumer
{
    public static async Task Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        ITenantCustomerBackfillService backfill,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerDirectoryEntry> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            await backfill.EnsureBackfilledAsync(evt.TenantId, ct);

            await UpsertAsync(evt, repository, ct);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry upserted for tenant {TenantId}, customer {CustomerId} (Created).",
                evt.TenantId,
                evt.CustomerId
            );
        }
    }

    private static async Task UpsertAsync(
        CustomerCreatedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
        if (existing is not null)
        {
            // El backfill, paginando en paralelo con este evento, ya pudo haber creado la fila.
            existing.ApplyIfNewer(evt.DisplayName, CustomerDirectoryStatus.Active, evt.OccurredOn);
            return;
        }

        var entry = CustomerDirectoryEntry.Create(
            evt.TenantId,
            evt.CustomerId,
            evt.DisplayName,
            CustomerDirectoryStatus.Active,
            evt.OccurredOn
        );
        await repository.AddAsync(entry, ct);
    }
}
