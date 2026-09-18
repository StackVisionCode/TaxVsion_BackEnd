using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Sms.Application.Messages.Commands;
using Wolverine;

namespace TaxVision.Sms.Application.Messages.Consumers;

/// <summary>
/// Ejecutor de canal <b>SMS</b> del orquestador Campaigns (ADR-CAMP-001 D3: consumer dentro del
/// servicio existente <c>TaxVision.Sms</c>, reusa <c>SendSmsBatchCommand</c>). Consume
/// <c>campaign.dispatch.requested.v1</c> filtrando <c>Channel==Sms</c> y responde
/// <c>campaign.dispatch.result.v1</c> en cascada. Mapea el estado del envío: proveedor aceptó →
/// <c>Accepted</c> (el <c>Delivered</c> real llega por DLR/webhook, fase posterior); opt-out →
/// <c>Skipped</c>; rechazo → <c>Failed</c>. SIN dinero (los segmentos son dato de facturación, no
/// los cotiza Campaign).
/// </summary>
public static class CampaignSmsDispatchConsumer
{
    public static async Task<CampaignDispatchResultIntegrationEvent?> Handle(
        CampaignDispatchRequestedIntegrationEvent evt,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.Channel, "Sms", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(evt.PhoneE164))
            return ToResult(evt, "Skipped", providerRef: null, reason: "no_destination");

        var send = await bus.InvokeAsync<Result<SendSmsBatchResponse>>(
            new SendSmsBatchCommand(
                evt.TenantId,
                evt.CorrelationId,
                [
                    new SmsSendItemDto(
                        ResolveCustomerId(evt.ContactRef),
                        evt.PhoneE164!,
                        evt.Body ?? string.Empty,
                        Media: null,
                        IdempotencyKey: evt.DispatchId,
                        SourceContext: $"campaign:{evt.RunId:N}"
                    ),
                ]
            ),
            ct
        );

        if (send.IsFailure)
            return ToResult(evt, "Failed", providerRef: null, reason: send.Error.Code);

        var item = send.Value.Results.Count > 0 ? send.Value.Results[0] : null;
        var outcome = item?.Status switch
        {
            "Accepted" => "Accepted",
            "Suppressed" => "Skipped",
            _ => "Failed",
        };
        return ToResult(evt, outcome, item?.ProviderMessageId, item?.ErrorCode);
    }

    /// <summary>SMS pide un CustomerId Guid; si el <c>ContactRef</c> ya es un Guid, se usa; si no, se deriva determinísticamente (opt-out/idempotencia estables).</summary>
    private static Guid ResolveCustomerId(string contactRef)
    {
        if (Guid.TryParse(contactRef, out var parsed))
            return parsed;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(contactRef ?? string.Empty));
        return new Guid(hash.AsSpan(0, 16));
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
