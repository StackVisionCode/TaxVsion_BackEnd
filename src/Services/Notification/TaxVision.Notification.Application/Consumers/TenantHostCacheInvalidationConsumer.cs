using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.PlatformIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Descarta el host cacheado cuando deja de ser válido, para que el próximo correo no salga con un
/// link al host viejo durante los 15 minutos de la cache: una oficina renombró su subdominio (se olvida
/// esa) o la plataforma cambió de dominio (se olvidan todas).
/// </summary>
public static class TenantHostCacheInvalidationConsumer
{
    public static void Handle(
        TenantSubdomainChangedIntegrationEvent evt,
        TenantHostCache cache,
        ILogger<TenantHostCache> logger
    )
    {
        cache.Invalidate(evt.TenantId);
        logger.LogInformation(
            "Tenant host cache dropped for tenant {TenantId} after a subdomain rename.",
            evt.TenantId
        );
    }

    public static void Handle(
        PlatformSettingsChangedIntegrationEvent evt,
        TenantHostCache cache,
        ILogger<TenantHostCache> logger
    )
    {
        cache.Clear();
        logger.LogInformation("Tenant host cache cleared after platform settings v{Version}.", evt.Version);
    }
}
