using System.Text.Json;
using System.Text.Json.Nodes;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Email.Sending.Campaign;
using TaxVision.Notification.Application.Email.Webhooks;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Webhook único de SMTP2GO (el plan gratis permite uno solo). Anónimo — lo llama el proveedor, sin
/// JWT; se protege con el secreto compartido <c>CampaignEmail:WebhookSecret</c>. Cada aviso va a su
/// dueño mirando solo el Message-ID: los de campaña siguen el camino de siempre hacia Campaigns, los
/// de Postmaster (<c>pm.</c>) se reenvían crudos para que Postmaster los traduzca, el resto se ignora.
/// </summary>
[ApiController]
[Route("notifications/campaign-email/webhooks")]
[AllowAnonymous]
public sealed class CampaignEmailWebhookController(
    IIntegrationEventPublisher publisher,
    IProviderWebhookMetrics metrics,
    IConfiguration configuration,
    ILogger<CampaignEmailWebhookController> logger
) : ControllerBase
{
    private const string Source = "Smtp2Go";

    /// <summary>Un aviso pesa ~2 KB; esto deja margen sin aceptar cuerpos gigantes de un tercero.</summary>
    private const int MaxBodyBytes = 256 * 1024;

    [HttpPost("smtp2go")]
    [RequestSizeLimit(MaxBodyBytes)]
    [RateLimitExempt(
        "Anónimo — webhook del proveedor SMTP2GO sin JWT que particionar. No se limita por IP: el tráfico "
            + "legítimo llega en ráfagas (una campaña genera miles de avisos) y cortarlo perdería entregas. "
            + "Lo protegen el secreto compartido y el tope de tamaño."
    )]
    public async Task<IActionResult> Smtp2Go(CancellationToken ct)
    {
        var check = SharedSecretWebhookVerifier.Verify(Request, configuration["CampaignEmail:WebhookSecret"]);
        if (check.Verdict == WebhookSecretVerdict.NotConfigured)
        {
            logger.LogError("SMTP2GO webhook rejected because CampaignEmail:WebhookSecret is not configured.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (check.Source == WebhookSecretSource.Query)
            logger.LogWarning("SMTP2GO webhook used the legacy query-string secret. Prefer Authorization bearer.");

        if (check.Verdict != WebhookSecretVerdict.Accepted)
            return Unauthorized();

        if (!Request.HasFormContentType && !Request.HasJsonContentType())
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);

        var events = await ReadEventsAsync(ct);
        if (events is null)
            return BadRequest();

        foreach (var evt in events)
            await RouteAsync(evt, ct);

        return Ok();
    }

    private async Task RouteAsync(JsonObject evt, CancellationToken ct)
    {
        var messageId = Field(evt, "message-id", "message_id", "messageid");
        var route = ProviderWebhookRouter.Classify(
            messageId,
            Field(evt, ProviderWebhookRouter.SystemCorrelationHeader)
        );
        switch (route)
        {
            case ProviderWebhookRoute.Campaign:
                await PublishCampaignResultAsync(evt, messageId!, ct);
                break;
            case ProviderWebhookRoute.SystemEmail:
                // Notification no traduce nada de esto: la ACL vive en Postmaster.
                await publisher.PublishAsync(
                    new EmailProviderFeedbackReceivedIntegrationEvent
                    {
                        TenantId = PlatformTenant.Id,
                        CorrelationId = Guid.NewGuid().ToString("N"),
                        Source = Source,
                        RawPayload = evt.ToJsonString(),
                        ReceivedAtUtc = DateTime.UtcNow,
                    },
                    ct
                );
                break;
            default:
                metrics.RecordUnrouted(Source);
                break;
        }
    }

    private async Task PublishCampaignResultAsync(JsonObject evt, string messageId, CancellationToken ct)
    {
        CampaignEmailCorrelation.TryParse(
            messageId,
            out var tenantId,
            out var campaignId,
            out var runId,
            out var dispatchId
        );
        var eventType = Field(evt, "event");
        if (string.IsNullOrWhiteSpace(eventType))
            return;

        var (outcome, reason) = MapCampaignOutcome(eventType, Field(evt, "bounce", "reject", "rejectreason", "reason"));
        if (outcome is null)
            return; // evento sin interés (processed/open/click/…): no cambia el estado de entrega

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
    }

    /// <summary>
    /// SMTP2GO manda un evento por POST, en JSON o form según la config. Se acepta también un arreglo
    /// por si el proveedor los agrupa. Null si el JSON no se puede leer.
    /// </summary>
    private async Task<IReadOnlyList<JsonObject>?> ReadEventsAsync(CancellationToken ct)
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            var fromForm = new JsonObject();
            foreach (var (key, value) in form)
                fromForm[key] = value.ToString();
            return [fromForm];
        }

        JsonNode? root;
        try
        {
            root = await JsonNode.ParseAsync(Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return null;
        }

        return root switch
        {
            JsonObject single => [single],
            JsonArray many => many.OfType<JsonObject>().ToList(),
            _ => null,
        };
    }

    /// <summary>SMTP2GO: event=delivered → Delivered; bounce/reject → Failed. El resto no toca la entrega.</summary>
    private static (string? Outcome, string? Reason) MapCampaignOutcome(string eventType, string? reasonDetail) =>
        eventType.Trim().ToLowerInvariant() switch
        {
            "delivered" => ("Delivered", null),
            "bounce" => ("Failed", string.IsNullOrWhiteSpace(reasonDetail) ? "bounce" : $"bounce:{reasonDetail}"),
            "reject" => ("Failed", string.IsNullOrWhiteSpace(reasonDetail) ? "reject" : $"reject:{reasonDetail}"),
            "spam" => ("Failed", "spam"),
            _ => (null, null),
        };

    /// <summary>Lee el primer campo presente; los nombres de headers personalizados llegan con cualquier mayúscula.</summary>
    private static string? Field(JsonObject evt, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var (name, value) in evt)
            {
                if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase) || value is null)
                    continue;

                var text =
                    value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var str)
                        ? str
                        : value.ToJsonString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }
        return null;
    }
}
