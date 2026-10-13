using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Tenants.Abstractions;

namespace TaxVision.Tenant.Application.Tenants.Consumers;

/// <summary>
/// Auth renombró el subdominio primario de una oficina: el registro de oficinas refleja el slug nuevo.
/// Sin esto el slug viejo seguía acá (y en el listado de PlatformAdmin) aunque la oficina ya entrara
/// por el nuevo. Idempotente: si el slug ya es el nuevo, no hace nada.
/// </summary>
public static class TenantSubdomainChangedConsumer
{
    public static async Task Handle(
        TenantSubdomainChangedIntegrationEvent evt,
        ITenantRepository tenants,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        ICorrelationContext correlation,
        ILogger<TenantSubdomainChangedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        if (!evt.IsPrimary)
            return;

        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
        );

        var tenant = await tenants.GetByIdAsync(evt.TenantId, ct);
        if (tenant is null || tenant.SubDomain == evt.NewSlug)
            return;

        var result = tenant.ChangeSubDomain(evt.NewSlug);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Tenant {TenantId} subdomain rename to {NewSlug} rejected: {Error}",
                evt.TenantId,
                evt.NewSlug,
                result.Error.Message
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await TenantListCache.InvalidateAsync(cache, ct);
        logger.LogInformation(
            "Tenant {TenantId} subdomain renamed {OldSlug} -> {NewSlug}.",
            evt.TenantId,
            evt.OldSlug,
            evt.NewSlug
        );
    }
}
