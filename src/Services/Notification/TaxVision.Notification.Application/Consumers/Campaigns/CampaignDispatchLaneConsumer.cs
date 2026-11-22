using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Campaigns;

/// <summary>N8-03: el envío de campañas corre en el carril masivo; aquí solo se pasa el evento.</summary>
public static class CampaignDispatchLaneConsumer
{
    public static DispatchCampaignMessage? Handle(CampaignDispatchRequestedIntegrationEvent evt) =>
        IsHandledHere(evt.Channel) ? new DispatchCampaignMessage(evt.TenantId, evt) : null;

    // SMS lo envía su propio servicio.
    private static bool IsHandledHere(string? channel) =>
        string.Equals(channel, "Email", StringComparison.OrdinalIgnoreCase)
        || string.Equals(channel, "Push", StringComparison.OrdinalIgnoreCase);
}
