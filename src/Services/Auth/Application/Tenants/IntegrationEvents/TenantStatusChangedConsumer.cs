using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Tenants.IntegrationEvents;

public static class TenantStatusChangedConsumer
{
    public static async Task Handle(
        TenantStatusChangedIntegrationEvent evt,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            await tenants.SetActiveAsync(evt.ChangedTenantId, evt.IsActive, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}
