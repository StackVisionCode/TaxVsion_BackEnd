using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CustomerEvents;

/// <summary>
/// Reactivación del cliente ⇒ la proyección deja de estar archivada y vuelve a
/// aparecer en las búsquedas por email de P-14.
/// </summary>
public static class CustomerReactivatedConsumer
{
    public static async Task Handle(
        CustomerReactivatedIntegrationEvent evt,
        ICustomerEmailProjectionRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEmailProjection> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            var existing = await repository.GetByCustomerIdAsync(evt.TenantId, evt.CustomerId, ct);
            if (existing is null)
            {
                logger.LogInformation(
                    "CustomerEmailProjection not found for {CustomerId}; nothing to reactivate.",
                    evt.CustomerId
                );
                return;
            }

            existing.MarkReactivated();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(CustomerReactivatedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
