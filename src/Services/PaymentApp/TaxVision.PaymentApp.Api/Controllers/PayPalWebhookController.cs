using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>
/// Public PayPal webhook endpoint. PayPal authenticates with its signed webhook headers; the
/// Application handler verifies them through PayPal before parsing any business outcome.
/// </summary>
[ApiController]
[Route("payments-app/webhooks/paypal")]
[AllowAnonymous]
[EnableRateLimiting("webhooks")]
public sealed class PayPalWebhookController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [RateLimitExempt("Anonymous provider webhook; protected by PayPal signature verification and IP rate limit.")]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        string rawPayload;
        using (var reader = new StreamReader(Request.Body))
            rawPayload = await reader.ReadToEndAsync(ct);

        var result = await bus.InvokeAsync<Result>(
            new ProcessProviderWebhookCommand(
                PaymentProviderCode.PayPal,
                rawPayload,
                Request.Headers.ToDictionary(
                    header => header.Key,
                    header => header.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase
                )
            ),
            ct
        );

        return result.IsSuccess
            ? Ok()
            : StatusCode(result.Error.ToHttpStatusCode(), new { result.Error.Code, result.Error.Message });
    }
}
