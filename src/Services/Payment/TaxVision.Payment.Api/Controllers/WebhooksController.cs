using Microsoft.AspNetCore.Mvc;
using Stripe;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Application.SaaSPayments.Commands;
using Wolverine;

namespace TaxVision.Payment.Api.Controllers;

[ApiController]
[Route("webhooks")]
public sealed class WebhooksController(
    IStripeGateway stripeGateway,
    IMessageBus bus) : ControllerBase
{
    [HttpPost("stripe")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StripeWebhook(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var payload = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault() ?? string.Empty;
        var isValid = await stripeGateway.VerifyWebhookSignatureAsync(payload, signature, ct);
        if (!isValid)
            return BadRequest(new { error = "Invalid Stripe webhook signature." });

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ParseEvent(payload);
        }
        catch (StripeException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        switch (stripeEvent.Type)
        {
            case Events.PaymentIntentSucceeded:
            {
                if (stripeEvent.Data.Object is PaymentIntent intent)
                {
                    await bus.InvokeAsync<BuildingBlocks.Results.Result>(
                        new ProcessSaaSPaymentCommand(intent.Id, true, null), ct);
                }
                break;
            }
            case Events.PaymentIntentPaymentFailed:
            {
                if (stripeEvent.Data.Object is PaymentIntent intent)
                {
                    var reason = intent.LastPaymentError?.Message ?? "Payment failed.";
                    await bus.InvokeAsync<BuildingBlocks.Results.Result>(
                        new ProcessSaaSPaymentCommand(intent.Id, false, reason), ct);
                }
                break;
            }
        }

        return Ok();
    }
}
