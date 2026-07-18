using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Application.Projections.CustomerEvents;

/// <summary>
/// Archivado del cliente ⇒ soft-delete de la proyección, igual que Deactivated. El
/// archivado es un estado terminal más fuerte que la desactivación, pero para efectos
/// de esta proyección ambos significan lo mismo: dejar de matchear el remitente.
/// </summary>
public static class CustomerArchivedConsumer
{
    public static async Task Handle(
        CustomerArchivedIntegrationEvent evt,
        ICustomerEmailAddressRepository repository,
        ITenantCustomerBackfillService backfill,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEmailAddress> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            await backfill.EnsureBackfilledAsync(evt.TenantId, ct);

            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is null)
            {
                logger.LogInformation(
                    "CustomerEmailAddress not found for {CustomerId}; nothing to archive.",
                    evt.CustomerId
                );
                return;
            }

            existing.SoftDelete();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
