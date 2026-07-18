using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Projections;

/// <summary>Soft-delete de TenantLogoRef ante un TenantLogoRemovedIntegrationEvent. Invalida el L1 del logo del tenant.</summary>
public static class TenantLogoRemovedConsumer
{
    public static async Task Handle(
        TenantLogoRemovedIntegrationEvent evt,
        ITenantLogoRefRepository repository,
        IUnitOfWork unitOfWork,
        IMemoryCache l1Cache,
        ICorrelationContext correlation,
        ILogger<TenantLogoRef> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            var existing = await repository.GetByTenantIdAsync(evt.TenantId, ct);
            if (existing is null)
            {
                logger.LogInformation(
                    "TenantLogoRemoved skipped: no TenantLogoRef for tenant {TenantId}.",
                    evt.TenantId
                );
                return;
            }

            existing.MarkRemoved(evt.RemovedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
            l1Cache.Remove($"logo:{evt.TenantId}");

            logger.LogInformation("TenantLogoRef soft-deleted for tenant {TenantId}.", evt.TenantId);
        }
    }

    private static string ResolveCorrelationId(TenantLogoRemovedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
