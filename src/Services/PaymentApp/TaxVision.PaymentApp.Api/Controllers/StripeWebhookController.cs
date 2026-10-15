using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.ProviderCustomers.Commands.ProcessStripePaymentMethodWebhook;
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
public sealed class StripeWebhookController(IMessageBus bus, ILogger<StripeWebhookController> logger) : ControllerBase
{
    [HttpPost]
    [RateLimitExempt("Anónimo — conserva el limiter nativo webhooks (1000/min/IP), sin JWT que particionar.")]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        string rawPayload;
        using (var reader = new StreamReader(Request.Body))
            rawPayload = await reader.ReadToEndAsync(ct);

        var signatureHeader = Request.Headers["Stripe-Signature"].ToString();

        // Eventos de método guardado (checkout.session.completed con setup_future_usage=off_session y
        // payment_method.attached) corren por un handler paralelo que persiste el método tokenizado
        // en TenantProviderCustomer. Side effect: NO reemplaza al flujo principal — el mismo
        // checkout.session.completed debe seguir reconciliando el SaaSPayment.
        if (IsPaymentMethodEvent(rawPayload))
        {
            var pmResult = await bus.InvokeAsync<Result>(
                new ProcessStripePaymentMethodWebhookCommand(rawPayload, signatureHeader),
                ct
            );
            if (pmResult.IsFailure)
                logger.LogWarning(
                    "Stripe payment-method webhook handler failed; main flow continues. Error={ErrorCode}: {ErrorMessage}",
                    pmResult.Error.Code,
                    pmResult.Error.Message
                );
        }

        var result = await bus.InvokeAsync<Result>(new ProcessStripeWebhookCommand(rawPayload, signatureHeader), ct);

        if (result.IsSuccess)
            return Ok();
        if (ProviderWebhookThrottle.IsThrottled(result.Error))
            return await ProviderWebhookThrottle.RespondAsync(HttpContext, ct);
        return BadRequest(new { result.Error.Code, result.Error.Message });
    }

    private static bool IsPaymentMethodEvent(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
            return false;
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            if (!document.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                return false;
            var eventType = type.GetString();
            return string.Equals(eventType, "checkout.session.completed", StringComparison.Ordinal)
                || string.Equals(eventType, "payment_method.attached", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
