using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Application.Brands.Commands;

/// <summary>
/// Puente al modelo viejo de eventos: solo el logo de la superficie CRM alimenta el email (Scribe).
/// Al quitarlo (delete o reset) se publica <see cref="TenantLogoRemovedIntegrationEvent"/> con el
/// MISMO contrato que el modelo viejo, para que Scribe deje de mostrarlo. El portal y el favicon no
/// viajan a Scribe.
/// </summary>
internal static class BrandLogoEvents
{
    public static async Task PublishRemovedIfCrmLogoAsync(
        IMessageBus bus,
        ICorrelationContext correlation,
        Guid tenantId,
        BrandSurface surface,
        BrandAssetKey key
    )
    {
        if (surface != BrandSurface.Crm || key != BrandAssetKey.Logo)
            return;

        await bus.PublishAsync(
            new TenantLogoRemovedIntegrationEvent
            {
                TenantId = tenantId,
                RemovedAtUtc = DateTime.UtcNow,
                CorrelationId = correlation.CorrelationId,
            }
        );
    }
}
