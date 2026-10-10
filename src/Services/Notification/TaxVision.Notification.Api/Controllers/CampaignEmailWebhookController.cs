using System.Text.Json;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Email.Sending.Campaign;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Webhook de estado del proveedor de email de CAMPAÑA (SMTP2GO y cualquier proveedor que haga POST de
/// eventos). Anónimo — lo firma/llama el proveedor, no pasa por el JWT; se protege con un secreto
/// compartido opcional (header <c>X-Webhook-Secret</c> o <c>?key=</c>). Correlaciona por el Message-Id
/// (SMTP2GO devuelve el del remitente) que el adapter codificó con tenant/campaign/run/dispatch, y
/// publica un <c>campaign.dispatch.result.v1</c> de Delivered/Failed que el consumer de Campaigns aplica
/// (refinamiento tardío Accepted→Delivered). Siempre responde 200 para no inducir reintentos del proveedor.
/// </summary>
[ApiController]
[Route("notifications/campaign-email/webhooks")]
[AllowAnonymous]
public sealed class CampaignEmailWebhookController(
    IIntegrationEventPublisher publisher,
    IConfiguration configuration,
    ILogger<CampaignEmailWebhookController> logger
) : ControllerBase
{
    [HttpPost("smtp2go")]
    public async Task<IActionResult> Smtp2Go(CancellationToken ct)
    {
        var expectedSecret = configuration["CampaignEmail:WebhookSecret"];
        if (!string.IsNullOrWhiteSpace(expectedSecret) && !SecretMatches(expectedSecret))
            return Unauthorized();

        var (eventType, messageId, reasonDetail) = await ReadPayloadAsync(ct);

        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(messageId))
        {
            logger.LogWarning("SMTP2GO webhook: missing event/message-id; ignoring.");
            return Ok();
        }

        if (!CampaignEmailCorrelation.TryParse(messageId, out var tenantId, out var campaignId, out var runId, out var dispatchId))
        {
            // Un Message-Id que no es de campaña (otro tráfico por la misma cuenta SMTP2GO) — no es nuestro.
            logger.LogDebug("SMTP2GO webhook: message-id '{MessageId}' is not a campaign send; ignoring.", messageId);
            return Ok();
        }

        var (outcome, reason) = MapOutcome(eventType, reasonDetail);
        if (outcome is null)
            return Ok(); // evento sin interés (processed/open/click/…): no cambia el estado de entrega

        await publisher.PublishAsync(
            new CampaignDispatchResultIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = Guid.NewGuid().ToString("N"),
                CampaignId = campaignId,
                RunId = runId,
                DispatchId = dispatchId,
                Outcome = outcome,
                ProviderRef = messageId,
                Reason = reason,
            },
            ct
        );

        logger.LogInformation(
            "SMTP2GO webhook: {Event} → {Outcome} for dispatch {DispatchId} (run {RunId}).",
            eventType,
            outcome,
            dispatchId,
            runId
        );
        return Ok();
    }

    /// <summary>Valida el secreto compartido. SMTP2GO lo manda como <c>Authorization: Bearer &lt;token&gt;</c>
    /// (ver panel del webhook); se aceptan además <c>X-Webhook-Secret</c> y <c>?key=</c> como alternativas.</summary>
    private bool SecretMatches(string expected)
    {
        var auth = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(auth[bearer.Length..].Trim(), expected, StringComparison.Ordinal))
            return true;

        if (string.Equals(Request.Headers["X-Webhook-Secret"].ToString().Trim(), expected, StringComparison.Ordinal))
            return true;

        return string.Equals(Request.Query["key"].ToString().Trim(), expected, StringComparison.Ordinal);
    }

    /// <summary>Lee el payload sea form-encoded o JSON (SMTP2GO lo manda en cualquiera de los dos según config).</summary>
    private async Task<(string? Event, string? MessageId, string? Reason)> ReadPayloadAsync(CancellationToken ct)
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            return (
                First(form, "event"),
                First(form, "message-id", "message_id", "messageid"),
                First(form, "bounce", "reject", "rejectreason", "reason")
            );
        }

        using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
        var root = doc.RootElement;
        return (
            JsonStr(root, "event"),
            JsonStr(root, "message-id", "message_id", "messageid"),
            JsonStr(root, "bounce", "reject", "rejectreason", "reason")
        );
    }

    /// <summary>SMTP2GO: event=delivered → Delivered; bounce/reject → Failed. El resto no toca la entrega.</summary>
    private static (string? Outcome, string? Reason) MapOutcome(string eventType, string? reasonDetail) =>
        eventType.Trim().ToLowerInvariant() switch
        {
            "delivered" => ("Delivered", null),
            "bounce" => ("Failed", string.IsNullOrWhiteSpace(reasonDetail) ? "bounce" : $"bounce:{reasonDetail}"),
            "reject" => ("Failed", string.IsNullOrWhiteSpace(reasonDetail) ? "reject" : $"reject:{reasonDetail}"),
            "spam" => ("Failed", "spam"),
            _ => (null, null),
        };

    private static string? First(Microsoft.AspNetCore.Http.IFormCollection form, params string[] keys)
    {
        foreach (var k in keys)
            if (form.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.ToString();
        return null;
    }

    private static string? JsonStr(JsonElement root, params string[] keys)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var k in keys)
            if (root.TryGetProperty(k, out var el))
                return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        return null;
    }
}
