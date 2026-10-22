using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using BuildingBlocks.Web.RateLimiting;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Api.Options;
using TaxVision.Connectors.Application.Sync;
using Wolverine;

namespace TaxVision.Connectors.Api.Controllers;

/// <summary>
/// Push subscription de Gmail (Pub/Sub) — público, autenticado únicamente por el JWT OIDC que
/// Google firma en cada request (nunca por sesión/tenant, no hay uno acá). Metadata-first (Fase 7,
/// §19): nunca descarga body ni attachments, solo dispara el fetch de historial+metadata.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("connectors/webhooks")]
[EnableRateLimiting("connectors-webhook")]
public sealed class GmailPushWebhookController(
    IMessageBus bus,
    ICorrelationContext correlation,
    IOptions<GmailPushWebhookOptions> options,
    ILogger<GmailPushWebhookController> logger
) : ControllerBase
{
    [HttpPost("gmail-push")]
    [RateLimitExempt(
        "Endpoint anonimo (sin JWT propio, solo el JWT firmado por Google) — TieredRateLimitEvaluator "
            + "solo soporta particion por Tenant/User, asi que [RateLimit] fallaria abierto aqui. La "
            + "proteccion real la da el limiter nativo [EnableRateLimiting(\"connectors-webhook\")] "
            + "(IP, 100/min FixedWindow), que se deja intacto."
    )]
    public async Task<IActionResult> HandlePush([FromBody] PubSubPushEnvelope envelope, CancellationToken ct)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (!string.IsNullOrWhiteSpace(options.Value.ExpectedAudience))
                settings.Audience = [options.Value.ExpectedAudience];

            await GoogleJsonWebSignature.ValidateAsync(authHeader["Bearer ".Length..], settings);
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning(ex, "Gmail push webhook rejected — invalid Google-signed token.");
            return Unauthorized();
        }

        var decoded = TryDecodeMessage(envelope);
        if (
            decoded is null
            || string.IsNullOrWhiteSpace(decoded.EmailAddress)
            || string.IsNullOrWhiteSpace(decoded.HistoryId)
        )
            return BadRequest();

        // Se guarda en la cola durable y se responde: el sync corre después, fuera de la request.
        await bus.PublishAsync(
            new GmailPushReceived(decoded.EmailAddress, decoded.HistoryId, correlation.CorrelationId)
        );
        return Ok();
    }

    private GmailPushMessage? TryDecodeMessage(PubSubPushEnvelope envelope)
    {
        if (string.IsNullOrEmpty(envelope.Message?.Data))
            return null;

        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(envelope.Message.Data));
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "Gmail push webhook received a non-base64 message payload.");
            return null;
        }

        // Gmail manda historyId como NÚMERO JSON — se parsea vía GmailPushPayloadParser (JsonNode)
        // para no reventar deserializando un número en un string (antes daba 400 en cada push).
        var payload = GmailPushPayloadParser.Parse(json);
        if (payload is null)
        {
            logger.LogWarning("Gmail push webhook received an unparseable message payload.");
            return null;
        }

        return new GmailPushMessage { EmailAddress = payload.Value.EmailAddress, HistoryId = payload.Value.HistoryId };
    }

    public sealed record PubSubPushEnvelope
    {
        [JsonPropertyName("message")]
        public PubSubMessage? Message { get; init; }
    }

    public sealed record PubSubMessage
    {
        [JsonPropertyName("data")]
        public string? Data { get; init; }
    }

    private sealed record GmailPushMessage
    {
        [JsonPropertyName("emailAddress")]
        public string? EmailAddress { get; init; }

        [JsonPropertyName("historyId")]
        public string? HistoryId { get; init; }
    }
}
