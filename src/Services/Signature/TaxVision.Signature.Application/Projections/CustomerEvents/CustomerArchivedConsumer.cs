using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CustomerEvents;

/// <summary>
/// Archivado del cliente ⇒ la proyección queda archivada para que P-14
/// no lo devuelva como coincidencia activa por email.
/// </summary>
public static class CustomerArchivedConsumer
{
    public static async Task Handle(
        CustomerArchivedIntegrationEvent evt,
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
                    "CustomerEmailProjection not found for {CustomerId}; nothing to archive.",
                    evt.CustomerId
                );
                return;
            }

            existing.MarkArchived();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(CustomerArchivedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
