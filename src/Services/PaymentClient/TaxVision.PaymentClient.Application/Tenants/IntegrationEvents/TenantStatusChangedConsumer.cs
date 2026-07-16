using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Application.Tenants.IntegrationEvents;

/// <summary>Mantiene la proyección local al día para que <c>TenantStatusGateMiddleware</c>
/// pueda rechazar tenants Suspended/Closed sin ir a Auth.</summary>
public static class TenantStatusChangedConsumer
{
    public static async Task Handle(
        TenantStatusChangedIntegrationEvent evt,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            await tenants.UpdateStatusAsync(evt.ChangedTenantId, evt.Status, evt.IsActive, DateTime.UtcNow, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
