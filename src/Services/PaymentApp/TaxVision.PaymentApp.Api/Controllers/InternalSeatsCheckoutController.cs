using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SeatsCheckouts.Commands;
using TaxVision.PaymentApp.Application.SeatsCheckouts.Queries;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>M2M-only: Subscription invoca este endpoint (StartSeatCheckout) para crear la sesión de checkout
/// hosteada de una compra de asientos cuando el tenant no tiene método en archivo.</summary>
[ApiController]
[Route("internal/seats")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalSeatsCheckoutController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateSeatsCheckoutRequest(
        Guid TenantId,
        Guid SeatPurchaseIntentId,
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
        "M2M ServiceOnly — invocado por Subscription (StartSeatCheckout); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<SeatsCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCheckout(CreateSeatsCheckoutRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SeatsCheckoutResponse>>(
            new CreateSeatsCheckoutCommand(
                request.TenantId,
                request.SeatPurchaseIntentId,
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

    /// <summary>Estado de un pago de asientos — lo consulta el job de reconciliación de Subscription para
    /// aprovisionar intenciones que quedaron Pending pese a un pago confirmado (evento de resultado perdido).</summary>
    [HttpGet("payments/{saaSPaymentId:guid}")]
    [RateLimitExempt("M2M ServiceOnly — invocado por el reconcile de Subscription; nunca expuesto al Gateway público.")]
    [ProducesResponseType<SeatPaymentStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentStatus(
        Guid saaSPaymentId,
        [FromQuery] Guid tenantId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<SeatPaymentStatusResponse>>(
            new GetSeatPaymentStatusQuery(tenantId, saaSPaymentId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
