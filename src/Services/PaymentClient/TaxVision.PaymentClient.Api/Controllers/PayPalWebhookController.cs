using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ProcessTenantWebhook;
using TaxVision.PaymentClient.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers;

/// <summary>
/// Webhooks de PayPal de una oficina (reembolsos, reversos y disputas). Endpoint anónimo: PayPal firma
/// con los encabezados <c>PAYPAL-*</c> y <see cref="ProcessTenantWebhookHandler"/> los verifica contra la
/// API de PayPal con el Webhook ID y las claves de esa oficina. Exento de <c>TenantStatusGateMiddleware</c>
/// (prefijo <c>/payments-client/webhooks</c>). Cualquier respuesta no 2xx hace que PayPal reintente.
/// </summary>
[ApiController]
[Route("payments-client/webhooks/{tenantId:guid}/paypal")]
[AllowAnonymous]
[EnableRateLimiting("webhooks")]
public sealed class PayPalWebhookController(IMessageBus bus) : ControllerBase
{
    private const string PayPalHeaderPrefix = "PAYPAL-";

    [HttpPost(Name = PaymentWebhookRoutes.PayPal)]
    [RateLimitExempt(
        "Anónimo — conserva el limiter nativo \"webhooks\" (1000 req/min/IP), verificado contra la API de PayPal con el Webhook ID de la oficina dentro de ProcessTenantWebhookHandler."
    )]
    public async Task<IActionResult> Receive(Guid tenantId, CancellationToken ct)
    {
        string rawPayload;
        using (var reader = new StreamReader(Request.Body))
            rawPayload = await reader.ReadToEndAsync(ct);

        // Solo los encabezados de firma de PayPal; el cuerpo se reenvía tal cual para verificarlo.
        var headers = Request
            .Headers.Where(header => header.Key.StartsWith(PayPalHeaderPrefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var result = await bus.InvokeAsync<Result>(
            new ProcessTenantWebhookCommand(tenantId, PaymentProviderCode.PayPal, rawPayload, headers),
            ct
        );

        return result.IsSuccess ? Ok() : BadRequest(new { result.Error.Code, result.Error.Message });
    }
}
