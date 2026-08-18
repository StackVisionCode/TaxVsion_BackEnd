using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Domain.Projections;

namespace TaxVision.Calendar.Application.Projections.CustomerEvents;

public static class CustomerUpdatedConsumer
{
    public static async Task Handle(
        CustomerUpdatedIntegrationEvent evt,
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
                "CustomerDirectoryEntry synced for tenant {TenantId}, customer {CustomerId} (Updated).",
                evt.TenantId,
                evt.CustomerId
            );
        }
    }

    private static async Task UpsertAsync(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
        if (existing is not null)
        {
            // Update sólo sincroniza el nombre; el status no le corresponde a este evento.
            existing.ApplyIfNewer(evt.DisplayName, existing.Status, evt.OccurredOn);
            return;
        }

        // Se autocura: un tenant descubierto por este mismo evento puede no haber traído la fila si
        // el paginado de Customer ya reflejaba el update.
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
