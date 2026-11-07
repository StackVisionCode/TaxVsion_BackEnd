using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.SaaSPayments.Queries;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>M2M-only, solo lectura (GRW-1): Growth verifica contra la fila del pago que un canje tardío tiene el pago
/// confirmado antes de darlo por bueno.</summary>
[ApiController]
[Route("internal/code-reservations")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalCodeReservationPaymentsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("payment-outcome")]
    [RateLimitExempt(
        "M2M ServiceOnly — lo llama Growth al verificar un commit tardío; nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<CodeReservationPaymentOutcomeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentOutcome(
        [FromQuery] string paymentSource,
        [FromQuery] Guid paymentId,
        [FromQuery] Guid codeReservationId,
        [FromQuery] Guid? onboardingId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<CodeReservationPaymentOutcomeResponse>>(
            new GetCodeReservationPaymentOutcomeQuery(paymentSource, paymentId, codeReservationId, onboardingId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
