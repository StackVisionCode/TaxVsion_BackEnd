using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Projections;

/// <summary>Upsert de TenantLogoRef ante un TenantLogoUpdatedIntegrationEvent. Invalida el L1 del logo del tenant.</summary>
public static class TenantLogoUpdatedConsumer
{
    public static async Task Handle(
        TenantLogoUpdatedIntegrationEvent evt,
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
                var logoRef = TenantLogoRef.Create(
                    evt.TenantId,
                    evt.CloudStorageFileId,
                    evt.ContentType,
                    evt.SizeBytes,
                    evt.Width,
                    evt.Height,
                    evt.UpdatedAtUtc
                );
                await repository.AddAsync(logoRef, ct);
            }
            else
            {
                existing.Update(
                    evt.CloudStorageFileId,
                    evt.ContentType,
                    evt.SizeBytes,
                    evt.Width,
                    evt.Height,
                    evt.UpdatedAtUtc
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
            l1Cache.Remove($"logo:{evt.TenantId}");

            logger.LogInformation("TenantLogoRef upserted for tenant {TenantId}.", evt.TenantId);
        }
    }

    private static string ResolveCorrelationId(TenantLogoUpdatedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
