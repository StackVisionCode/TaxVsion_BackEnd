using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Email.Templates;
using TaxVision.Notification.Domain.Emailing.Campaigns;
using TaxVision.Notification.Domain.Emailing.Sending;
using Wolverine;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// Dispatcher del fan-out: divide los destinatarios de una campaña en lotes y publica un evento por lote
/// (procesamiento por lotes para no abrir una transacción gigante). Fuera del request HTTP.
/// </summary>
public static class EmailCampaignStartedConsumer
{
    private const int BatchSize = 100;

    public static async Task Handle(
        EmailCampaignStartedIntegrationEvent evt,
        IEmailCampaignRepository campaigns,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        ILogger<EmailCampaignStartedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var campaign = await campaigns.GetByIdNoRecipientsAsync(evt.CampaignId, ct);
            if (
                campaign is null
                || campaign.Status != CampaignStatus.Running
                || string.IsNullOrEmpty(campaign.HtmlTemplate)
            )
            {
                logger.LogWarning("Campaign {CampaignId} not ready for fan-out.", evt.CampaignId);
                return;
            }

            for (var skip = 0; skip < campaign.TotalRecipients; skip += BatchSize)
                await bus.PublishAsync(
                    new EmailCampaignBatchIntegrationEvent
                    {
                        CampaignId = campaign.Id,
                        Skip = skip,
                        Take = BatchSize,
                        TenantId = campaign.TenantId,
                        CorrelationId = correlation.CorrelationId,
                    }
                );

            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

/// <summary>
/// Procesa un lote de destinatarios: renderiza desde la fuente capturada (sin CloudStorage) y crea un
/// correo saliente por destinatario, encolando su entrega. Los contadores de la campaña se actualizan
/// vía los eventos de entrega (los fallos de render publican un EmailDeliveryFailed sintético).
/// </summary>
public static class EmailCampaignBatchConsumer
{
    public static async Task Handle(
        EmailCampaignBatchIntegrationEvent evt,
        IEmailCampaignRepository campaigns,
        IOutboundEmailRepository outbound,
        ITemplateRenderer renderer,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        ILogger<EmailCampaignBatchIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var campaign = await campaigns.GetByIdNoRecipientsAsync(evt.CampaignId, ct);
            if (campaign is null || string.IsNullOrEmpty(campaign.HtmlTemplate))
                return;

            var allowed = EmailTemplateMapper.ParseVariables(campaign.AllowedVariablesJson);
            var recipients = await campaigns.GetRecipientsPageAsync(evt.CampaignId, evt.Skip, evt.Take, ct);

            foreach (var recipient in recipients)
            {
                var variables = Deserialize(recipient.VariablesJson);
                var rendered = renderer.Render(
                    new RenderRequest(
                        campaign.SubjectTemplate ?? string.Empty,
                        campaign.HtmlTemplate!,
                        null,
                        variables,
                        allowed
                    )
                );
                if (rendered.IsFailure)
                {
                    logger.LogWarning(
                        "Render failed for {Address} in campaign {CampaignId}: {Error}",
                        recipient.Address,
                        campaign.Id,
                        rendered.Error.Message
                    );
                    await PublishRenderFailureAsync(
                        bus,
                        campaign.TenantId,
                        campaign.Id,
                        correlation.CorrelationId,
                        rendered.Error.Message
                    );
                    continue;
                }

                var html = rendered.Value.HtmlBody;
                if (!string.IsNullOrEmpty(campaign.LayoutHtml))
                {
                    var wrapped = renderer.ApplyLayout(campaign.LayoutHtml!, rendered.Value.HtmlBody);
                    if (wrapped.IsSuccess)
                        html = wrapped.Value;
                }

                var messageResult = OutboundEmailMessage.Create(
                    campaign.TenantId,
                    rendered.Value.Subject,
                    html,
                    rendered.Value.TextBody,
                    EmailPriority.Normal,
                    [(recipient.Address, EmailRecipientKind.To, recipient.Name)],
                    "[]",
                    campaign.TemplateId,
                    campaign.TemplateVersionId,
                    campaign.Id,
                    correlation.CorrelationId
                );
                if (messageResult.IsFailure)
                {
                    await PublishRenderFailureAsync(
                        bus,
                        campaign.TenantId,
                        campaign.Id,
                        correlation.CorrelationId,
                        messageResult.Error.Message
                    );
                    continue;
                }

                await outbound.AddAsync(messageResult.Value, ct);
                await bus.PublishAsync(
                    new EmailSendRequestedIntegrationEvent
                    {
                        MessageId = messageResult.Value.Id,
                        TenantId = campaign.TenantId,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static ValueTask PublishRenderFailureAsync(
        IMessageBus bus,
        Guid tenantId,
        Guid campaignId,
        string correlationId,
        string error
    ) =>
        // Sin correo que entregar: se cuenta como fallo vía el consumer de entrega para no dejar la campaña incompleta.
        bus.PublishAsync(
            new EmailDeliveryFailedIntegrationEvent
            {
                MessageId = Guid.NewGuid(),
                TenantId = tenantId,
                CorrelationId = correlationId,
                Error = error,
                CampaignId = campaignId,
            }
        );

    private static Dictionary<string, string?> Deserialize(string variablesJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(variablesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>Actualiza los contadores de la campaña cuando un correo suyo se entrega con éxito.</summary>
public static class CampaignDeliverySucceededConsumer
{
    public static async Task Handle(
        EmailDeliverySucceededIntegrationEvent evt,
        IEmailCampaignRepository campaigns,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (evt.CampaignId is not { } campaignId)
            return;

        var campaign = await campaigns.GetForProcessingAsync(campaignId, ct);
        if (campaign is null)
            return;

        var wasRunning = campaign.Status == CampaignStatus.Running;
        campaign.IncrementSent();
        await PublishIfCompletedAsync(campaign, wasRunning, bus, correlation, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    internal static async Task PublishIfCompletedAsync(
        EmailCampaign campaign,
        bool wasRunning,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (wasRunning && campaign.Status == CampaignStatus.Completed)
            await bus.PublishAsync(
                new EmailCampaignCompletedIntegrationEvent
                {
                    CampaignId = campaign.Id,
                    TenantId = campaign.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SentCount = campaign.SentCount,
                    FailedCount = campaign.FailedCount,
                }
            );
    }
}

/// <summary>Actualiza los contadores de la campaña cuando un correo suyo falla.</summary>
public static class CampaignDeliveryFailedConsumer
{
    public static async Task Handle(
        EmailDeliveryFailedIntegrationEvent evt,
        IEmailCampaignRepository campaigns,
        IMessageBus bus,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (evt.CampaignId is not { } campaignId)
            return;

        var campaign = await campaigns.GetForProcessingAsync(campaignId, ct);
        if (campaign is null)
            return;

        var wasRunning = campaign.Status == CampaignStatus.Running;
        campaign.IncrementFailed();
        await CampaignDeliverySucceededConsumer.PublishIfCompletedAsync(campaign, wasRunning, bus, correlation, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
