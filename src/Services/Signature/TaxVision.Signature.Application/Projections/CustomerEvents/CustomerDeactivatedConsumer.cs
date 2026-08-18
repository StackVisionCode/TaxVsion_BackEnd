using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections.CustomerEvents;

/// <summary>
/// Desactivación del cliente ⇒ la proyección queda marcada como no-activa (mismo flag que
/// <see cref="CustomerArchivedConsumer"/>) para que la regla P-14 no lo devuelva como coincidencia
/// activa por email. La desactivación es reversible: <see cref="CustomerActivatedConsumer"/> la
/// deshace. Antes de esta corrección Signature solo escuchaba <c>customer.archived.v1</c>, así que
/// un cliente meramente desactivado seguía apareciendo como coincidencia activa (hallazgo de la
/// auditoría de proyecciones de customer, 2026-08-06).
/// </summary>
public static class CustomerDeactivatedConsumer
{
    public static async Task Handle(
        CustomerDeactivatedIntegrationEvent evt,
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
                    "CustomerEmailProjection not found for {CustomerId}; nothing to deactivate.",
                    evt.CustomerId
                );
                return;
            }

            existing.MarkArchived();
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static string ResolveCorrelationId(CustomerDeactivatedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
