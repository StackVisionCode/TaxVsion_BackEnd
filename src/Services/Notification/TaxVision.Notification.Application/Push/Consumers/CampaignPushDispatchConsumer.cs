using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;
using Wolverine;

namespace TaxVision.Notification.Application.Push.Consumers;

/// <summary>
/// Ejecutor de canal <b>Push</b> del orquestador Campaigns (mismo patrón que Email/SMS: consumer en el
/// servicio dueño del canal, reusa <see cref="NotificationDispatcher.SendPushAsync"/> → FCM/APNs).
/// Consume <c>campaign.dispatch.requested.v1</c> filtrando <c>Channel==Push</c> y responde
/// <c>campaign.dispatch.result.v1</c>. Push va a un <b>usuario</b> con dispositivos registrados: el
/// <c>ContactRef</c> debe ser su <c>UserId</c> (los contactos con email/teléfono no tienen token de push,
/// por eso una audiencia de contactos cae en <c>Skipped</c>). Mapea: enviado/permitido → Accepted; sin
/// dispositivo → Skipped(no_device); rechazo → Failed. SIN dinero.
/// </summary>
public static class CampaignPushDispatchConsumer
{
    private const string TemplateKey = "campaign.push";

    public static async Task<CampaignDispatchResultIntegrationEvent?> Handle(
        CampaignDispatchRequestedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.Channel, "Push", StringComparison.OrdinalIgnoreCase))
            return null; // otro canal — no es lo nuestro

        // El destino de Push es un usuario con dispositivos; el ContactRef debe ser un UserId (Guid).
        if (!Guid.TryParse(evt.ContactRef, out var userId) || userId == Guid.Empty)
            return ToResult(evt, "Skipped", "push_requires_user_id");

        var title = string.IsNullOrWhiteSpace(evt.Subject) ? "TaxProffice" : evt.Subject!;
        var body = evt.Body ?? string.Empty;

        var result = await dispatcher.SendPushAsync(
            evt.TenantId,
            userId,
            title,
            body,
            NotificationCategory.Collaboration,
            TemplateKey,
            relatedEventId: null,
            correlationId: evt.CorrelationId,
            ct
        );

        if (result.IsSuccess)
            return ToResult(evt, "Accepted", null);

        var outcome = result.Error.Code == "Notification.NoPushDevices" ? "Skipped" : "Failed";
        return ToResult(evt, outcome, result.Error.Code);
    }

    private static CampaignDispatchResultIntegrationEvent ToResult(
        CampaignDispatchRequestedIntegrationEvent evt,
        string outcome,
        string? reason
    ) =>
        new()
        {
            TenantId = evt.TenantId,
            CorrelationId = evt.CorrelationId,
            CampaignId = evt.CampaignId,
            RunId = evt.RunId,
            DispatchId = evt.DispatchId,
            Outcome = outcome,
            ProviderRef = null,
            Reason = reason,
        };
}
