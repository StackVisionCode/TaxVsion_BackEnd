using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Backfill;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Domain.Projections;

namespace TaxVision.Calendar.Application.Projections.CustomerEvents;

// Los siete consumers de este grupo mantienen CustomerDirectoryEntry. Todos devuelven en cascada
// BackfillTenantCustomersCommand si el tenant no se ha sembrado: recibir cualquier evento de Customer
// es la única forma en que Task descubre que un tenant existe. El HTTP a Customer va en ese comando.

public static class CustomerCreatedConsumer
{
    public static async Task<BackfillTenantCustomersCommand?> Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        ITenantBackfillStateRepository backfillStates,
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
            var backfill = await BackfillTenantCustomersCommand.IfMissingAsync(evt.TenantId, backfillStates, ct);

            await UpsertAsync(evt, repository, ct);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry upserted for tenant {TenantId}, customer {CustomerId} (Created).",
                evt.TenantId,
                evt.CustomerId
            );
            return backfill;
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
