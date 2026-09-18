using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Email.Sending.Commands;
using TaxVision.Notification.Domain.Emailing.Sending;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Sending.Consumers;

/// <summary>
/// Ejecutor de canal <b>Email</b> del orquestador Campaigns (ADR-CAMP-001 D3: consumer dentro del
/// servicio existente <c>Notification</c>, reusa <c>SendEmailCommand</c>). Consume
/// <c>campaign.dispatch.requested.v1</c> filtrando <c>Channel==Email</c> (el fanout entrega todo) y
/// responde <c>campaign.dispatch.result.v1</c> en cascada. <c>Accepted</c> = encolado por
/// Notification; el <c>Delivered</c> real (webhook Postmaster) se correlacionará en una fase
/// posterior. SIN dinero.
/// </summary>
public static class CampaignEmailDispatchConsumer
{
    public static async Task<CampaignDispatchResultIntegrationEvent?> Handle(
        CampaignDispatchRequestedIntegrationEvent evt,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.Channel, "Email", StringComparison.OrdinalIgnoreCase))
            return null; // otro canal — no es lo nuestro

        if (string.IsNullOrWhiteSpace(evt.Email))
            return ToResult(evt, "Skipped", providerRef: null, reason: "no_destination");

        var body = evt.Body ?? string.Empty;
        var send = await bus.InvokeAsync<Result<OutboundEmailResponse>>(
            new SendEmailCommand(
                evt.TenantId,
                string.IsNullOrWhiteSpace(evt.Subject) ? "(no subject)" : evt.Subject!,
                body,
                body,
                EmailPriority.Normal,
                [new EmailRecipientInput(evt.Email!)],
                AttachmentFileIds: null
            ),
            ct
        );

        return send.IsSuccess
            ? ToResult(evt, "Accepted", send.Value.Id.ToString(), reason: null)
            : ToResult(evt, "Failed", providerRef: null, reason: send.Error.Code);
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
