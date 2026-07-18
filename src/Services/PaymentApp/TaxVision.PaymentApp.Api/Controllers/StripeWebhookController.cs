using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessStripeWebhook;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>
/// Endpoint público sin JWT — Stripe autentica con la firma HMAC en el header
/// <c>Stripe-Signature</c>, verificada dentro de <see cref="ProcessStripeWebhookHandler"/>.
/// Exento de <c>TenantStatusGateMiddleware</c> (ver <c>ExemptPathPrefixes</c>): el tenant se
/// resuelve del payload, no del JWT. Rate limitado a 1000 req/min/IP (§K.1).
/// </summary>
[ApiController]
[Route("payments-app/webhooks/stripe")]
[AllowAnonymous]
[EnableRateLimiting("webhooks")]
public sealed class StripeWebhookController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        string rawPayload;
        using (var reader = new StreamReader(Request.Body))
            rawPayload = await reader.ReadToEndAsync(ct);

        var signatureHeader = Request.Headers["Stripe-Signature"].ToString();

        var result = await bus.InvokeAsync<Result>(new ProcessStripeWebhookCommand(rawPayload, signatureHeader), ct);

        return result.IsSuccess ? Ok() : BadRequest(new { result.Error.Code, result.Error.Message });
    }
}
