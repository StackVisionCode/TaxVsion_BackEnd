using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CustomerEvents;

/// <summary>
/// Activación del cliente ⇒ contraparte reversible de <see cref="CustomerDeactivatedConsumer"/>:
/// la proyección deja de estar marcada como no-activa y vuelve a aparecer en las búsquedas por
/// email de la regla P-14. Reusa el mismo flag que <see cref="CustomerReactivatedConsumer"/>.
/// </summary>
public static class CustomerActivatedConsumer
{
    public static async Task Handle(
        CustomerActivatedIntegrationEvent evt,
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
                    "CustomerEmailProjection not found for {CustomerId}; nothing to activate.",
                    evt.CustomerId
                );
                return;
            }

            existing.MarkReactivated();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(CustomerActivatedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
