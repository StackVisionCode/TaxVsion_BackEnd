using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.HostedCheckout.Commands;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>M2M-only: Subscription invoca este endpoint cuando el usuario cierra la pasarela sin pagar, para
/// expirar la sesión hosteada antes del vencimiento natural. Idempotente — devolver <c>alreadyExpired=true</c>
/// es un éxito tanto si el provider dijo que ya estaba expirada como si el pago local ya está en estado
/// terminal.</summary>
[ApiController]
[Route("internal/hosted-checkout")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalHostedCheckoutController(IMessageBus bus, ILogger<InternalHostedCheckoutController> logger)
    : ControllerBase
{
    public sealed record CancelHostedCheckoutRequest(PaymentProviderCode? ProviderCode);

    [HttpPost("{providerSessionId}/cancel")]
    [RateLimitExempt(
        "M2M ServiceOnly — invocado por Subscription (cancel checkout); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<CancelHostedCheckoutSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(
        string providerSessionId,
        [FromBody] CancelHostedCheckoutRequest? body,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<CancelHostedCheckoutSessionResponse>>(
            new CancelHostedCheckoutSessionCommand(providerSessionId, body?.ProviderCode),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Cancel por <c>SaaSPaymentId</c> — Subscription usa este porque sus intenciones guardan el
    /// <c>SaaSPaymentId</c>, no el id de sesión del provider. Resuelve el sessionId y despacha al mismo handler.</summary>
    [HttpPost("payments/{saaSPaymentId:guid}/cancel")]
    [RateLimitExempt(
        "M2M ServiceOnly — invocado por Subscription (cancel checkout); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<CancelHostedCheckoutSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelByPaymentId(
        Guid saaSPaymentId,
        [FromQuery] Guid tenantId,
        ISaaSPaymentRepository payments,
        CancellationToken ct
    )
    {
        // SEC-L1: el token M2M lleva su propio tenantId. Si el query trae otro, el caller está
        // intentando cancelar un pago fuera de su scope o enumerar pagos ajenos — fail-closed con 403.
        if (!User.TryGetTenantId(out var callerTenantId) || callerTenantId == Guid.Empty)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new Error("SaaSPayment.Forbidden", "The caller token has no tenant scope.")
            );
        if (callerTenantId != tenantId)
        {
            logger.LogWarning(
                "Cancel checkout cross-tenant attempt: caller tenant {CallerTenantId} tried to cancel payment "
                    + "{PaymentId} for tenant {QueryTenantId}.",
                callerTenantId,
                saaSPaymentId,
                tenantId
            );
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new Error("SaaSPayment.Forbidden", "The caller tenant does not match the requested tenant.")
            );
        }

        var payment = await payments.GetByIdAsync(saaSPaymentId, tenantId, ct);
        if (payment is null)
            return StatusCode(
                StatusCodes.Status404NotFound,
                new Error("SaaSPayment.NotFound", "The payment does not exist for this tenant.")
            );

        if (string.IsNullOrWhiteSpace(payment.ProviderCheckoutSessionId))
            // Nunca llegó a crearse la sesión del provider: no hay qué cancelar, idempotente.
            return Ok(new CancelHostedCheckoutSessionResponse(AlreadyExpired: true));

        var result = await bus.InvokeAsync<Result<CancelHostedCheckoutSessionResponse>>(
            new CancelHostedCheckoutSessionCommand(payment.ProviderCheckoutSessionId, payment.ProviderCode),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
