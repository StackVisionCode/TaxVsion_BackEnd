using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.AddOnCheckouts.Commands;
using TaxVision.PaymentApp.Application.AddOnCheckouts.Queries;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>M2M-only: Subscription invoca este endpoint (StartAddOnCheckout) para crear la sesión de checkout
/// hosteada de la compra de un add-on cuando el tenant no tiene método en archivo. Molde:
/// <see cref="InternalSeatsCheckoutController"/>.</summary>
[ApiController]
[Route("internal/add-ons")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalAddOnCheckoutController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateAddOnCheckoutRequest(
        Guid TenantId,
        Guid AddOnPurchaseIntentId,
        long AmountCents,
        string Currency,
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string IdempotencyKey,
        /// <summary>Unidades y precio unitario del cobro, para el recibo. Opcionales: solo los llevan
        /// los cobros que tienen algo que contar, y PaymentApp los descarta si no cuadran.</summary>
        int? Quantity = null,
        long? UnitAmountCents = null,
        PaymentProviderCode? Provider = null,
        PaymentMethodKind? Method = null
    );

    [HttpPost("checkout")]
    [RateLimitExempt(
        "M2M ServiceOnly — invocado por Subscription (StartAddOnCheckout); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<AddOnCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCheckout(CreateAddOnCheckoutRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AddOnCheckoutResponse>>(
            new CreateAddOnCheckoutCommand(
                request.TenantId,
                request.AddOnPurchaseIntentId,
                request.AmountCents,
                request.Currency,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                request.IdempotencyKey,
                request.Quantity,
                request.UnitAmountCents,
                request.Provider ?? PaymentProviderCode.Stripe,
                request.Method ?? PaymentMethodKind.Card
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Estado de un pago de add-on — lo consulta el job de reconciliación de Subscription para activar
    /// intenciones que quedaron Pending pese a un pago confirmado (evento de resultado perdido).</summary>
    [HttpGet("payments/{saaSPaymentId:guid}")]
    [RateLimitExempt("M2M ServiceOnly — invocado por el reconcile de Subscription; nunca expuesto al Gateway público.")]
    [ProducesResponseType<AddOnPaymentStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentStatus(
        Guid saaSPaymentId,
        [FromQuery] Guid tenantId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<AddOnPaymentStatusResponse>>(
            new GetAddOnPaymentStatusQuery(tenantId, saaSPaymentId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
