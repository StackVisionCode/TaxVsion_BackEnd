using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.PaymentMethodSetup.Commands;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>M2M-only: Subscription invoca este endpoint para abrir una Checkout Session en modo "setup"
/// (sin cobro) que captura y reemplaza el método de pago del tenant. Molde:
/// <see cref="InternalSubscriptionRenewalCheckoutController"/>.</summary>
[ApiController]
[Route("internal/payment-methods")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalPaymentMethodSetupController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateSetupSessionRequest(
        Guid TenantId,
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string IdempotencyKey,
        string? Provider = null
    );

    [HttpPost("setup-session")]
    [RateLimitExempt(
        "M2M ServiceOnly — invocado por Subscription (StartPaymentMethodSetup); nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<PaymentMethodSetupSessionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateSetupSession(CreateSetupSessionRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PaymentMethodSetupSessionResponse>>(
            new CreatePaymentMethodSetupSessionCommand(
                request.TenantId,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                request.IdempotencyKey,
                request.Provider
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
