using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.PaymentClient.Application.TenantConnect.Commands.ProcessConnectWebhook;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

/// <summary>
/// Endpoint público sin JWT — a diferencia de <see cref="StripeWebhookController"/> (tenant
/// resuelto del path), acá no hay <c>{tenantId}</c>: Stripe no lo sabe, el tenant sale del
/// <c>StripeConnectAccountId</c> del payload, resuelto dentro de
/// <c>ProcessConnectWebhookHandler</c>. Verificado contra el webhook secret de PLATAFORMA
/// (<c>Stripe__ConnectWebhookSecret</c>), no el per-tenant. Rate limitado a 1000 req/min/IP
/// (§K.1).
/// </summary>
[ApiController]
[Route("payments-client/webhooks/stripe-connect")]
[AllowAnonymous]
[EnableRateLimiting("webhooks")]
public sealed class StripeConnectWebhookController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        string rawPayload;
        using (var reader = new StreamReader(Request.Body))
            rawPayload = await reader.ReadToEndAsync(ct);

        var signatureHeader = Request.Headers["Stripe-Signature"].ToString();

        var result = await bus.InvokeAsync<Result>(new ProcessConnectWebhookCommand(rawPayload, signatureHeader), ct);

        return result.IsSuccess ? Ok() : BadRequest(new { result.Error.Code, result.Error.Message });
    }
}
