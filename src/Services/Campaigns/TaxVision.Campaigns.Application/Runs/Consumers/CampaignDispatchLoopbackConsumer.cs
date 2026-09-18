using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using Microsoft.Extensions.Configuration;

namespace TaxVision.Campaigns.Application.Runs.Consumers;

/// <summary>
/// Ejecutor <b>loopback</b> de prueba (Fase 1 del plan): consume <c>campaign.dispatch.requested.v1</c>
/// y devuelve <c>Delivered</c> — valida la saga dispatch→result→cierre end-to-end sin un proveedor
/// real. Se apaga con el flag <c>Campaigns:LoopbackExecutor:Enabled</c> (default OFF) cuando entran
/// los ejecutores reales (Email en Notification, SMS en TaxVision.Sms). No toca dinero.
/// </summary>
public static class CampaignDispatchLoopbackConsumer
{
    public static CampaignDispatchResultIntegrationEvent? Handle(
        CampaignDispatchRequestedIntegrationEvent evt,
        IConfiguration configuration
    )
    {
        if (!configuration.GetValue<bool>("Campaigns:LoopbackExecutor:Enabled"))
            return null;

        // Mensaje en cascada — Wolverine lo publica al exchange (requiere su PublishMessage<> en Program).
        return new CampaignDispatchResultIntegrationEvent
        {
            TenantId = evt.TenantId,
            CorrelationId = evt.CorrelationId,
            CampaignId = evt.CampaignId,
            RunId = evt.RunId,
            DispatchId = evt.DispatchId,
            Outcome = "Delivered",
            ProviderRef = "loopback",
        };
    }
}
