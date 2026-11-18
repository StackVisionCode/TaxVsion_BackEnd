using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Backfill;
using TaxVision.Tasks.Application.Backfill.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Domain.Projections;
using Wolverine;

namespace TaxVision.Tasks.Application.Projections.CustomerEvents;

public static class CustomerUpdatedConsumer
{
    public static async Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerDirectoryRepository repository,
        ITenantCustomerBackfillService backfill,
        IMessageBus bus,
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
            await TenantCustomerBackfillRequest.RequestIfNeededAsync(evt.TenantId, backfill, bus, ct);

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
        var status = ParseStatus(evt.Status);
        if (existing is not null)
        {
            // Nombre y email por su hora; el estado solo si el aviso lo trae (E-3), con la suya.
            existing.ApplyProfileIfNewer(evt.DisplayName, evt.PrimaryEmail, evt.OccurredOn);
            if (status is { } known)
                existing.ApplyStatusIfNewer(known, evt.OccurredOn);
            return;
        }

        // Se autocura: un tenant descubierto por este mismo evento puede no haber traído la fila si
        // el paginado de Customer ya reflejaba el update.
        var entry = CustomerDirectoryEntry.Create(
            evt.TenantId,
            evt.CustomerId,
            evt.DisplayName,
            status ?? CustomerDirectoryStatus.Active,
            evt.OccurredOn,
            evt.PrimaryEmail
        );
        await repository.AddAsync(entry, ct);
    }

    // Null en mensajes viejos, que no traen el estado.
    private static CustomerDirectoryStatus? ParseStatus(string? status) =>
        Enum.TryParse<CustomerDirectoryStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
}
