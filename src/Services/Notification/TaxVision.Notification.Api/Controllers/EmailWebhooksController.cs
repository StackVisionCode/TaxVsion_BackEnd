using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Email.Sending.Commands;
using Wolverine;

namespace TaxVision.Notification.Api.Controllers;

/// <summary>
/// Recibe eventos de tracking de proveedores (delivered/opened/clicked/bounced) en un formato normalizado
/// y los aplica a los correos salientes. Anónimo pero autenticado por secreto compartido (los adaptadores
/// por proveedor —SendGrid/Mailgun— traducirian su formato a este payload normalizado).
/// </summary>
[ApiController]
[Route("notifications/email/webhooks")]
public sealed class EmailWebhooksController(IMessageBus bus, IOptions<EmailWebhookOptions> options) : ControllerBase
{
    public sealed record TrackingEvent(Guid MessageId, EmailTrackingEventType Type, string? Detail = null);

    public sealed record TrackingBatch(IReadOnlyList<TrackingEvent> Events);

    [HttpPost("tracking")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Tracking(
        [FromHeader(Name = "X-Webhook-Secret")] string? secret,
        [FromBody] TrackingBatch batch,
        CancellationToken ct
    )
    {
        if (!IsValidSecret(secret))
            return Unauthorized();

        if (batch?.Events is null || batch.Events.Count == 0)
            return BadRequest();

        foreach (var evt in batch.Events)
            await bus.InvokeAsync<Result>(new ApplyEmailTrackingEventCommand(evt.MessageId, evt.Type, evt.Detail), ct);

        return NoContent();
    }

    private bool IsValidSecret(string? provided)
    {
        var configured = options.Value.Secret;
        return !string.IsNullOrEmpty(configured)
            && !string.IsNullOrEmpty(provided)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(configured),
                Encoding.UTF8.GetBytes(provided)
            );
    }
}
