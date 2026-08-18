using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Projections.CustomerEvents;

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

            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is not null)
            {
                // Update no cambia el status (Active/Inactive/Archived) — solo sincroniza el nombre.
                existing.ApplyIfNewer(evt.DisplayName, existing.Status, evt.OccurredOn);
            }
            else
            {
                // No debería pasar tras un backfill exitoso, pero un tenant recién descubierto
                // por este mismo evento puede no haber traído esta fila si Customer.Api ya
                // reflejaba el Update en el paginado — se autocura recreando con status Active.
                var entry = CustomerDirectoryEntry.Create(
                    evt.TenantId,
                    evt.CustomerId,
                    evt.DisplayName,
                    CustomerDirectoryStatus.Active,
                    evt.OccurredOn
                );
                await repository.AddAsync(entry, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "CustomerDirectoryEntry synced for tenant {TenantId}, customer {CustomerId} (Updated).",
                evt.TenantId,
                evt.CustomerId
            );
        }
    }
}
