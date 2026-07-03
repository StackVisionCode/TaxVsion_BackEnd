using Microsoft.AspNetCore.Mvc;
using Stripe;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Application.SaaSPayments.Commands;
using Wolverine;

namespace TaxVision.Payment.Api.Controllers;

/// <summary>
/// Receives verified webhook events from Stripe and dispatches them to the Application layer.
/// <para>
/// The endpoint is anonymous — authentication is performed by validating the
/// <c>Stripe-Signature</c> header via <see cref="IStripeGateway.VerifyWebhookSignatureAsync"/>.
/// The <c>Stripe:WebhookSecret</c> used for verification must be set via environment variable.
/// </para>
/// Supported events:
/// <list type="bullet">
///   <item><c>payment_intent.succeeded</c> → transitions the matching SaaS payment to <c>Completed</c>.</item>
///   <item><c>payment_intent.payment_failed</c> → transitions it to <c>Failed</c>.</item>
/// </list>
/// </summary>
[ApiController]
[Route("webhooks")]
public sealed class WebhooksController(
    IStripeGateway stripeGateway,
    IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Stripe webhook receiver. Validates the <c>Stripe-Signature</c> header before processing.
    /// Returns <c>200 OK</c> for all recognized events (including no-op cases) to prevent
    /// Stripe from re-delivering. Returns <c>400 Bad Request</c> only on signature failure.
    /// </summary>
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
