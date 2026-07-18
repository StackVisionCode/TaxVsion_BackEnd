using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
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
    IOptions<GmailPushWebhookOptions> options,
    ILogger<GmailPushWebhookController> logger
) : ControllerBase
{
    [HttpPost("gmail-push")]
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

        var result = await bus.InvokeAsync<Result>(
            new ProcessGmailPushNotificationCommand(decoded.EmailAddress, decoded.HistoryId),
            ct
        );

        // Pub/Sub reintenta ante cualquier respuesta que no sea 2xx — "cuenta no encontrada" no es
        // transitorio (nunca lo va a encontrar reintentando), así que igual respondemos 200.
        if (result.IsSuccess || result.Error.Code == "TenantEmailAccount.NotFound")
            return Ok();

        logger.LogWarning("Gmail push processing failed: {Code} {Message}", result.Error.Code, result.Error.Message);
        return StatusCode(StatusCodes.Status500InternalServerError);
    }

    private GmailPushMessage? TryDecodeMessage(PubSubPushEnvelope envelope)
    {
        if (string.IsNullOrEmpty(envelope.Message?.Data))
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(envelope.Message.Data));
            return JsonSerializer.Deserialize<GmailPushMessage>(json);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            logger.LogWarning(ex, "Gmail push webhook received an unparseable message payload.");
            return null;
        }
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
