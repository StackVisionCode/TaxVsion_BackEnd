using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Projections;

/// <summary>
/// Mantiene la proyección <see cref="TenantBrandingRef"/> del certificado: el nombre de la oficina
/// llega en <c>TenantCreatedIntegrationEvent</c> y el logo en <c>TenantLogoUpdatedIntegrationEvent</c>
/// (Tenant lo publica al confirmarse el asset de la superficie CRM). Upsert idempotente por tenant.
/// </summary>
public static class TenantBrandingProjectionConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantBrandingRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantBrandingRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            var now = DateTime.UtcNow;
            var existing = await repository.GetByTenantIdAsync(evt.NewTenantId, ct);
            if (existing is null)
            {
                var branding = TenantBrandingRef.Create(evt.NewTenantId, now);
                branding.SetOfficeName(evt.Name, now);
                await repository.AddAsync(branding, ct);
            }
            else
            {
                existing.SetOfficeName(evt.Name, now);
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("TenantBrandingRef office name upserted for tenant {TenantId}.", evt.NewTenantId);
        }
    }

    public static async Task Handle(
        TenantLogoUpdatedIntegrationEvent evt,
        ITenantBrandingRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantBrandingRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            var existing = await repository.GetByTenantIdAsync(evt.TenantId, ct);
            if (existing is null)
            {
                var branding = TenantBrandingRef.Create(evt.TenantId, evt.UpdatedAtUtc);
                branding.SetLogo(evt.CloudStorageFileId, evt.ContentType, evt.SizeBytes, evt.UpdatedAtUtc);
                await repository.AddAsync(branding, ct);
            }
            else
            {
                existing.SetLogo(evt.CloudStorageFileId, evt.ContentType, evt.SizeBytes, evt.UpdatedAtUtc);
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("TenantBrandingRef logo upserted for tenant {TenantId}.", evt.TenantId);
        }
    }

    private static string ResolveCorrelationId(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
