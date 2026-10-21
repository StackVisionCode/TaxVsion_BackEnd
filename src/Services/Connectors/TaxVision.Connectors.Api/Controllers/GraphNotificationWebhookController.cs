using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Infrastructure.Providers.Watch;
using Wolverine;

namespace TaxVision.Connectors.Api.Controllers;

/// <summary>
/// Notificaciones de Graph subscriptions — público, autenticado por <c>clientState</c> (secreto
/// compartido, configurado al crear la subscription, Fase 6). Maneja también el handshake de
/// validación que Graph exige al crear/renovar cada subscription (echo del validationToken, texto
/// plano, sin auth — Graph lo llama antes de que exista ningún clientState que validar).
///
/// Graph espera la respuesta en ~3 s y degrada (y después descarta) los endpoints lentos, así que acá
/// solo se valida y se encola en una cola local durable: el sync y las renovaciones corren después.
/// Un 200 significa "recibido y guardado", no "procesado".
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("connectors/webhooks")]
[EnableRateLimiting("connectors-webhook")]
public sealed class GraphNotificationWebhookController(
    IMessageBus bus,
    IOptions<GraphWatchOptions> watchOptions,
    ICorrelationContext correlation,
    ILogger<GraphNotificationWebhookController> logger
) : ControllerBase
{
    [HttpPost("graph-notification")]
    [RateLimitExempt(
        "Endpoint anonimo (sin JWT, autenticado por clientState compartido) — TieredRateLimitEvaluator "
            + "solo soporta particion por Tenant/User, asi que [RateLimit] fallaria abierto aqui. La "
            + "proteccion real la da el limiter nativo [EnableRateLimiting(\"connectors-webhook\")] "
            + "(IP, 100/min FixedWindow), que se deja intacto."
    )]
    public Task<IActionResult> HandleNotification([FromQuery] string? validationToken, CancellationToken ct) =>
        HandleAsync(
            validationToken,
            notification => new GraphNotificationReceived(notification.SubscriptionId!, correlation.CorrelationId),
            ct
        );

    /// <summary>
    /// <c>lifecycleNotificationUrl</c> de las subscriptions: Graph avisa acá cuando hay que reautorizar,
    /// cuando borró la subscription o cuando perdió notificaciones. Mismo handshake y mismo clientState.
    /// </summary>
    [HttpPost("graph-lifecycle")]
    [RateLimitExempt(
        "Endpoint anonimo (sin JWT, autenticado por clientState compartido) — mismo criterio que "
            + "graph-notification: la proteccion la da [EnableRateLimiting(\"connectors-webhook\")]."
    )]
    public Task<IActionResult> HandleLifecycleNotification([FromQuery] string? validationToken, CancellationToken ct) =>
        HandleAsync(
            validationToken,
            notification =>
                string.IsNullOrWhiteSpace(notification.LifecycleEvent)
                    ? null
                    : new GraphLifecycleNotificationReceived(
                        notification.SubscriptionId!,
                        notification.LifecycleEvent,
                        correlation.CorrelationId
                    ),
            ct
        );

    private async Task<IActionResult> HandleAsync(
        string? validationToken,
        Func<GraphNotificationItem, object?> toMessage,
        CancellationToken ct
    )
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

            var message = toMessage(notification);
            if (message is not null)
                await bus.PublishAsync(message);
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

        /// <summary>Solo en los avisos de ciclo de vida.</summary>
        [JsonPropertyName("lifecycleEvent")]
        public string? LifecycleEvent { get; init; }
    }
}
