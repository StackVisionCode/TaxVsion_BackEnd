using BuildingBlocks.Messaging.PlatformIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.Platform;

/// <summary>
/// Camino rápido: aplica el cambio en cuanto Tenant lo publica. Si este mensaje se pierde (o hay
/// varias réplicas compitiendo por la misma cola), el refresco periódico lo alcanza igual.
/// </summary>
public static class PlatformSettingsChangedHandler
{
    public static void Handle(
        PlatformSettingsChangedIntegrationEvent evt,
        PlatformSettingsProvider provider,
        ILogger<PlatformSettingsProvider> logger
    )
    {
        var applied = provider.TryApply(
            new PlatformSettingsSnapshot(
                evt.Version,
                evt.BrandName,
                evt.LegalName,
                evt.BaseDomain,
                evt.ApiHost,
                evt.SupportEmail,
                evt.BillingEmail
            )
        );

        if (applied)
        {
            logger.LogInformation(
                "Platform settings v{Version} applied from event (brand={BrandName}, domain={BaseDomain}).",
                evt.Version,
                evt.BrandName,
                evt.BaseDomain
            );
        }
    }
}
