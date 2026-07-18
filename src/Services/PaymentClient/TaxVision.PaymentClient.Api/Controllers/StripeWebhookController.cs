using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ProcessTenantWebhook;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

/// <summary>
/// Endpoint público sin JWT — Stripe autentica con la firma HMAC en el header
/// <c>Stripe-Signature</c>, verificada dentro de <see cref="ProcessTenantWebhookHandler"/>
/// contra el <c>WebhookSecretEncrypted</c> DEL TENANT resuelto acá desde el path (a
/// diferencia de PaymentApp, donde hay un solo secret global). Exento de
/// <c>TenantStatusGateMiddleware</c> (ver <c>ExemptPathPrefixes</c>). Rate limitado a
/// 1000 req/min/IP (§K.1).
/// </summary>
[ApiController]
[Route("payments-client/webhooks/{tenantId:guid}/stripe")]
[AllowAnonymous]
[EnableRateLimiting("webhooks")]
public sealed class StripeWebhookController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(Guid tenantId, CancellationToken ct)
    {
        string rawPayload;
        using (var reader = new StreamReader(Request.Body))
            rawPayload = await reader.ReadToEndAsync(ct);

        var signatureHeader = Request.Headers["Stripe-Signature"].ToString();

        var result = await bus.InvokeAsync<Result>(
            new ProcessTenantWebhookCommand(tenantId, PaymentProviderCode.Stripe, rawPayload, signatureHeader),
            ct
        );

        return result.IsSuccess ? Ok() : BadRequest(new { result.Error.Code, result.Error.Message });
    }
}
