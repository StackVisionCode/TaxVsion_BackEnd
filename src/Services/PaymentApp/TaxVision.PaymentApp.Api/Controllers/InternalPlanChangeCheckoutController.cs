using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.PlanChangeCheckouts.Commands;
using TaxVision.PaymentApp.Application.PlanChangeCheckouts.Queries;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>M2M-only: Subscription invoca este endpoint (ChangePlan) para cobrar un upgrade por redirect
/// cuando el tenant no tiene método en archivo. Molde: <see cref="InternalSeatsCheckoutController"/>.</summary>
[ApiController]
[Route("internal/plan-change")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalPlanChangeCheckoutController(IMessageBus bus) : ControllerBase
{
    public sealed record CreatePlanChangeCheckoutRequest(
        Guid TenantId,
        Guid PlanChangeRequestId,
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
    [RateLimitExempt("M2M ServiceOnly — invocado por Subscription (ChangePlan); nunca expuesto al Gateway público.")]
    [ProducesResponseType<PlanChangeCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCheckout(CreatePlanChangeCheckoutRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PlanChangeCheckoutResponse>>(
            new CreatePlanChangeCheckoutCommand(
                request.TenantId,
                request.PlanChangeRequestId,
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

    /// <summary>Estado de un pago de upgrade — lo consulta Subscription para el poll de la pantalla.</summary>
    [HttpGet("payments/{saaSPaymentId:guid}")]
    [RateLimitExempt("M2M ServiceOnly — invocado por Subscription; nunca expuesto al Gateway público.")]
    [ProducesResponseType<PlanChangePaymentStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentStatus(
        Guid saaSPaymentId,
        [FromQuery] Guid tenantId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<PlanChangePaymentStatusResponse>>(
            new GetPlanChangePaymentStatusQuery(tenantId, saaSPaymentId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
