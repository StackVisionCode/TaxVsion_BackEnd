using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts.Commands;
using TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts.Queries;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>M2M-only: Subscription invoca este endpoint (StartRenewalCheckout) para crear la sesión de checkout
/// hosteada de una renovación/reactivación self-service cuando el tenant no tiene método en archivo.
/// Molde: <see cref="InternalSeatsCheckoutController"/>.</summary>
[ApiController]
[Route("internal/subscription-renewal")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalSubscriptionRenewalCheckoutController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateSubscriptionRenewalCheckoutRequest(
        Guid TenantId,
        Guid RenewalIntentId,
        long AmountCents,
        string Currency,
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string IdempotencyKey,
        PaymentProviderCode? Provider = null,
        PaymentMethodKind? Method = null
    );

    [HttpPost("checkout")]
    [RateLimitExempt(
        "M2M ServiceOnly — invocado por Subscription (StartRenewalCheckout); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<SubscriptionRenewalCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCheckout(
        CreateSubscriptionRenewalCheckoutRequest request,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<SubscriptionRenewalCheckoutResponse>>(
            new CreateSubscriptionRenewalCheckoutCommand(
                request.TenantId,
                request.RenewalIntentId,
                request.AmountCents,
                request.Currency,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                request.IdempotencyKey,
                request.Provider ?? PaymentProviderCode.Stripe,
                request.Method ?? PaymentMethodKind.Card
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Estado de un pago de renovación — lo consulta el job de reconciliación de Subscription para
    /// reactivar intenciones que quedaron Pending pese a un pago confirmado (evento de resultado perdido).</summary>
    [HttpGet("payments/{saaSPaymentId:guid}")]
    [RateLimitExempt("M2M ServiceOnly — invocado por el reconcile de Subscription; nunca expuesto al Gateway público.")]
    [ProducesResponseType<SubscriptionRenewalPaymentStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentStatus(
        Guid saaSPaymentId,
        [FromQuery] Guid tenantId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<SubscriptionRenewalPaymentStatusResponse>>(
            new GetSubscriptionRenewalPaymentStatusQuery(tenantId, saaSPaymentId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
