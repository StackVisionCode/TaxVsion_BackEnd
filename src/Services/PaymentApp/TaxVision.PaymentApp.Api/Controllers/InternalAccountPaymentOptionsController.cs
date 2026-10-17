using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.AccountPaymentOptions;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>M2M-only: Subscription invoca este endpoint para el picker del Account del Landing. Antes
/// de este endpoint, cualquier compra manual del Landing (add-on, seats, renewal, plan change) iba a
/// Stripe por default y rompia a tenants que onboardearon con PayPal — PaymentApp lo expone acá para
/// que Subscription proxyfica al landing y pueda preguntar al usuario.</summary>
[ApiController]
[Route("internal/account")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalAccountPaymentOptionsController(IMessageBus bus) : ControllerBase
{
    [HttpGet("payment-options")]
    [RateLimitExempt(
        "M2M ServiceOnly — invocado por Subscription (GetAccountPaymentOptions); nunca expuesto al Gateway publico."
    )]
    [ProducesResponseType<AccountPaymentOptionsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentOptions(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AccountPaymentOptionsResponse>>(
            new GetAccountPaymentOptionsQuery(),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
