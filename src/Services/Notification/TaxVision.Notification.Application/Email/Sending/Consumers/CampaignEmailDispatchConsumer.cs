using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Email.Sending.Campaign;

namespace TaxVision.Notification.Application.Email.Sending.Consumers;

/// <summary>
/// Ejecutor de canal <b>Email</b> del orquestador Campaigns (ADR-CAMP-001 D3: consumer dentro de
/// <c>Notification</c>). Consume <c>campaign.dispatch.requested.v1</c> filtrando <c>Channel==Email</c> y
/// responde <c>campaign.dispatch.result.v1</c> en cascada.
///
/// <para><b>Independiente de Postmaster (2026-10-09, pedido del usuario):</b> ya NO crea un
/// <c>OutboundEmailMessage</c> ni publica a Postmaster (ese camino era de prueba y además su resolución de
/// proveedor bloquea el stream Bulk de campañas a propósito). En su lugar envía DIRECTO por
/// <see cref="ICampaignEmailProvider"/>, que es <b>agnóstico de proveedor</b> (SMTP2GO hoy; intercambiable
/// por config <c>CampaignEmail:Provider</c>). <c>Accepted</c> = el proveedor aceptó el correo; el
/// <c>Delivered</c> real por webhook del proveedor es una fase posterior. SIN dinero (el cobro del Wallet
/// lo hace Campaigns en reserve/settle del run, no acá).</para>
/// </summary>
public static class CampaignEmailDispatchConsumer
{
    public static async Task<CampaignDispatchResultIntegrationEvent?> Handle(
        CampaignDispatchRequestedIntegrationEvent evt,
        ICampaignEmailProvider emailProvider,
        ILogger<CampaignEmailMessage> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.Channel, "Email", StringComparison.OrdinalIgnoreCase))
            return null; // otro canal — no es lo nuestro

        if (string.IsNullOrWhiteSpace(evt.Email))
            return ToResult(evt, "Skipped", providerRef: null, reason: "no_destination");

        // Personalización por destinatario: {{first_name}}/{{full_name}}/{{email}}/… → datos del cliente.
        var subject = CampaignPersonalization.Render(evt.Subject, evt.RecipientName, evt.Email, evt.PhoneE164);
        var body =
            CampaignPersonalization.Render(evt.Body, evt.RecipientName, evt.Email, evt.PhoneE164) ?? string.Empty;

        var result = await emailProvider.SendAsync(
            new CampaignEmailMessage(
                evt.TenantId,
                evt.Email!,
                evt.RecipientName,
                string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject!,
                body,
                body, // text fallback = mismo contenido; el proveedor arma multipart
                evt.CampaignId,
                evt.RunId,
                evt.DispatchId
            ),
            ct
        );

        if (result.Accepted)
            return ToResult(evt, "Accepted", result.ProviderMessageId, reason: null);

        logger.LogWarning(
            "Campaign email dispatch {DispatchId} failed via {Provider}: {Code}",
            evt.DispatchId,
            emailProvider.Code,
            result.ErrorCode
        );
        return ToResult(evt, "Failed", providerRef: null, reason: result.ErrorCode);
    }

    private static CampaignDispatchResultIntegrationEvent ToResult(
        CampaignDispatchRequestedIntegrationEvent evt,
        string outcome,
        string? providerRef,
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
            ProviderRef = providerRef,
            Reason = reason,
        };
}
