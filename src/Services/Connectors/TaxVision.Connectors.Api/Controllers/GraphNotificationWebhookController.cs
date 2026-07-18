using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Infrastructure.Providers.Watch;
using Wolverine;

namespace TaxVision.Connectors.Api.Controllers;

/// <summary>
/// Notificaciones de Graph subscriptions — público, autenticado por <c>clientState</c> (secreto
/// compartido, configurado al crear la subscription, Fase 6). Maneja también el handshake de
/// validación que Graph exige al crear/renovar cada subscription (echo del validationToken, texto
/// plano, sin auth — Graph lo llama antes de que exista ningún clientState que validar).
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("connectors/webhooks")]
[EnableRateLimiting("connectors-webhook")]
public sealed class GraphNotificationWebhookController(
    IMessageBus bus,
    IOptions<GraphWatchOptions> watchOptions,
    ILogger<GraphNotificationWebhookController> logger
) : ControllerBase
{
    [HttpPost("graph-notification")]
    public async Task<IActionResult> HandleNotification([FromQuery] string? validationToken, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(validationToken))
            return Content(validationToken, "text/plain");

        // Graph no firma sus notificaciones (a diferencia de Stripe/GitHub) — clientState es el
        // único mecanismo real de autenticidad. Sin secreto configurado no hay nada que validar:
        // rechazar todo en vez de dejar pasar por una comparación vacía-contra-vacía.
        if (string.IsNullOrEmpty(watchOptions.Value.ClientState))
        {
            logger.LogWarning(
                "Graph notification webhook received a request but Connectors:Watch:Graph:ClientState is not configured — rejecting."
            );
            return Unauthorized();
        }

        GraphNotificationEnvelope? envelope;
        try
        {
            envelope = await Request.ReadFromJsonAsync<GraphNotificationEnvelope>(ct);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Graph notification webhook received an unparseable payload.");
            return BadRequest();
        }

        foreach (var notification in envelope?.Value ?? [])
        {
            if (!HasValidClientState(notification.ClientState, watchOptions.Value.ClientState))
            {
                logger.LogWarning(
                    "Graph notification with mismatched clientState — discarded (possible spoofing attempt)."
                );
                continue;
            }

            if (string.IsNullOrWhiteSpace(notification.SubscriptionId))
                continue;

            var result = await bus.InvokeAsync<Result>(
                new ProcessGraphNotificationCommand(notification.SubscriptionId),
                ct
            );
            if (result.IsFailure && result.Error.Code != "ProviderWatchSubscription.NotFound")
                logger.LogWarning(
                    "Graph notification processing failed: {Code} {Message}",
                    result.Error.Code,
                    result.Error.Message
                );
        }

        return Ok();
    }

    /// <summary>Comparación de tiempo constante — mismo patrón que Auth usa para client_credentials (evita timing attacks sobre el secreto).</summary>
    private static bool HasValidClientState(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected)
        );
    }

    public sealed record GraphNotificationEnvelope
    {
        [JsonPropertyName("value")]
        public List<GraphNotificationItem>? Value { get; init; }
    }

    public sealed record GraphNotificationItem
    {
        [JsonPropertyName("subscriptionId")]
        public string? SubscriptionId { get; init; }

        [JsonPropertyName("clientState")]
        public string? ClientState { get; init; }
    }
}
