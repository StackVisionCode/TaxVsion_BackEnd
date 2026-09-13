using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Application.Projections;

/// <summary>
/// Mantiene la proyección <see cref="TenantLogoRef"/>: Tenant publica <c>TenantLogoUpdatedIntegrationEvent</c>
/// al confirmarse el asset de logo de la superficie CRM, y <c>TenantLogoRemovedIntegrationEvent</c> al
/// borrarlo. Upsert idempotente por tenant. El PDF de factura usa este logo cuando el tenant no envía
/// un branding explícito (ver ProcessInvoiceGenerationHandler + ITenantLogoResolver).
/// </summary>
public static class TenantLogoProjectionConsumer
{
    public static async Task Handle(
        TenantLogoUpdatedIntegrationEvent evt,
        ITenantLogoRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantLogoRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            var existing = await repository.GetByTenantAsync(evt.TenantId, ct);
            if (existing is null)
            {
                var logoRef = TenantLogoRef.Create(evt.TenantId, evt.UpdatedAtUtc);
                logoRef.SetLogo(evt.CloudStorageFileId, evt.ContentType, evt.UpdatedAtUtc);
                await repository.AddAsync(logoRef, ct);
            }
            else
            {
                existing.SetLogo(evt.CloudStorageFileId, evt.ContentType, evt.UpdatedAtUtc);
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Documents TenantLogoRef updated for tenant {TenantId}.", evt.TenantId);
        }
    }

    public static async Task Handle(
        TenantLogoRemovedIntegrationEvent evt,
        ITenantLogoRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantLogoRef> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt.CorrelationId, evt.EventId)))
        {
            var existing = await repository.GetByTenantAsync(evt.TenantId, ct);
            if (existing is null)
                return;

            existing.ClearLogo(evt.RemovedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Documents TenantLogoRef cleared for tenant {TenantId}.", evt.TenantId);
        }
    }

    private static string ResolveCorrelationId(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
