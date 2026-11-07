using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Application.Email.Sending.Campaign;
using TaxVision.Notification.Domain.Emailing.Campaigns;
using Wolverine.Attributes;

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
///
/// <para>Sin transacción propia (NOT-09): la reserva por <c>DispatchId</c> se confirma antes de llamar al proveedor y
/// el resultado se guarda aparte. Una reserva sin resultado nunca se reenvía.</para>
/// </summary>
public static class CampaignEmailDispatchConsumer
{
    public const string OutcomeUnknownReason = "outcome_unknown";

    [NonTransactional]
    public static async Task<CampaignDispatchResultIntegrationEvent?> Handle(
        CampaignDispatchRequestedIntegrationEvent evt,
        ICampaignEmailProvider emailProvider,
        ICustomerContactPolicyRepository contactPolicies,
        ICampaignDispatchSendRepository sends,
        IUnitOfWork unitOfWork,
        IOptions<CampaignDispatchOptions> options,
        ILogger<CampaignEmailMessage> logger,
        CancellationToken ct
    )
    {
        if (!string.Equals(evt.Channel, "Email", StringComparison.OrdinalIgnoreCase))
            return null; // otro canal — no es lo nuestro

        if (string.IsNullOrWhiteSpace(evt.Email))
            return ToResult(evt, "Skipped", providerRef: null, reason: "no_destination");

        // Política de contacto del cliente (D-CMP-3). Un contacto que no es cliente no tiene fila y pasa.
        if (
            Guid.TryParse(evt.ContactRef, out var customerId)
            && !await CustomerContactPolicyFilter.AllowsAsync(
                evt.TenantId,
                customerId,
                CustomerContactChannel.Email,
                contactPolicies,
                ct
            )
        )
            return ToResult(evt, "Skipped", providerRef: null, reason: "contact_policy");

        var existing = await sends.FindAsync(evt.TenantId, evt.DispatchId, ct);
        if (existing is not null)
            return await ResolveExistingAsync(evt, existing, unitOfWork, options.Value, ct);

        var send = CampaignDispatchSend.Reserve(evt.TenantId, evt.DispatchId, DateTime.UtcNow);
        await sends.AddAsync(send, ct);
        await unitOfWork.SaveChangesAsync(ct);

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

        // El proveedor ya contestó: el resultado se guarda aunque se cancele el mensaje.
        if (result.Accepted)
        {
            send.MarkAccepted(result.ProviderMessageId, DateTime.UtcNow);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return ToResult(evt, "Accepted", result.ProviderMessageId, reason: null);
        }

        send.MarkFailed(result.ErrorCode, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        logger.LogWarning(
            "Campaign email dispatch {DispatchId} failed via {Provider}: {Code}",
            evt.DispatchId,
            emailProvider.Code,
            result.ErrorCode
        );
        return ToResult(evt, "Failed", providerRef: null, reason: result.ErrorCode);
    }

    // El mismo DispatchId en otro sobre: devuelve lo que ya pasó, sin volver a llamar al proveedor.
    private static async Task<CampaignDispatchResultIntegrationEvent> ResolveExistingAsync(
        CampaignDispatchRequestedIntegrationEvent evt,
        CampaignDispatchSend existing,
        IUnitOfWork unitOfWork,
        CampaignDispatchOptions options,
        CancellationToken ct
    )
    {
        switch (existing.Status)
        {
            case CampaignDispatchSendStatus.Accepted:
                return ToResult(evt, "Accepted", existing.ProviderMessageId, reason: null);
            case CampaignDispatchSendStatus.Failed:
                return ToResult(evt, "Failed", providerRef: null, reason: existing.FailureReason);
        }

        if (DateTime.UtcNow - existing.CreatedAtUtc < options.OutcomeUnknownAfter)
            throw new DependencyNotReadyException(
                nameof(CampaignDispatchSend),
                $"Dispatch {evt.DispatchId} has no result yet."
            );

        existing.MarkFailed(OutcomeUnknownReason, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return ToResult(evt, "Failed", providerRef: null, reason: OutcomeUnknownReason);
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
