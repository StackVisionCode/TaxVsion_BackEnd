using System.Text.Json;
using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.PaymentApp.Application.ProviderCustomers.Commands.ProcessPayPalVaultWebhook;
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

        var headers = Request.Headers.ToDictionary(
            header => header.Key,
            header => header.Value.ToString(),
            StringComparer.OrdinalIgnoreCase
        );

        // Vault token events corren por un handler separado: no corresponden a un SaaSPayment,
        // mutan TenantProviderCustomer. El dispatcher genérico los marcaría Rejected por no
        // encontrar cargo asociado.
        if (IsVaultEvent(rawPayload))
        {
            var vaultResult = await bus.InvokeAsync<Result>(
                new ProcessPayPalVaultWebhookCommand(rawPayload, headers),
                ct
            );
            if (vaultResult.IsSuccess)
                return Ok();
            return StatusCode(
                vaultResult.Error.ToHttpStatusCode(),
                new { vaultResult.Error.Code, vaultResult.Error.Message }
            );
        }

        var result = await bus.InvokeAsync<Result>(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, rawPayload, headers),
            ct
        );

        if (result.IsSuccess)
            return Ok();
        if (ProviderWebhookThrottle.IsThrottled(result.Error))
            return await ProviderWebhookThrottle.RespondAsync(HttpContext, ct);
        return StatusCode(result.Error.ToHttpStatusCode(), new { result.Error.Code, result.Error.Message });
    }

    private static bool IsVaultEvent(string rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
            return false;
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            return document.RootElement.TryGetProperty("event_type", out var eventType)
                && eventType.ValueKind == JsonValueKind.String
                && (eventType.GetString() ?? string.Empty).StartsWith("VAULT.", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
