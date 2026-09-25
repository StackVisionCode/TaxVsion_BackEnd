using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>
/// Respuesta a un webhook throttleado por tenant: 429 con <c>Retry-After</c> y el contrato común de
/// rechazo. Stripe y PayPal reintentan cualquier respuesta no-2xx con su propio backoff; un 200 les
/// decía que el evento estaba entregado y lo perdían.
/// </summary>
internal static class ProviderWebhookThrottle
{
    public const string Policy = "paymentapp.webhook_tenant";

    public static bool IsThrottled(Error error) => error.Code == ProcessProviderWebhookHandler.WebhookThrottledCode;

    public static async Task<IActionResult> RespondAsync(HttpContext context, CancellationToken ct)
    {
        NativeRateLimitMetrics.RecordRejected(Policy);
        await RateLimitRejection.WriteAsync(
            context,
            ProcessProviderWebhookHandler.WebhookThrottleRetryAfterSeconds,
            Policy,
            layer: "tenant",
            ct
        );
        return new EmptyResult();
    }
}
