using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Onboarding.TokenReferences.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow (Fase 9) — M2M-only: expone el raw registration token guardado en Redis por
/// <c>OnboardingPaymentSucceededConsumer</c>, para que otro servicio (Notification, vía Scribe)
/// construya el email de bienvenida con el link real. One-shot: la referencia se consume (se
/// borra) en la misma llamada — un segundo intento con la misma referencia siempre falla.</summary>
[ApiController]
[Route("internal/onboarding/tokens")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalOnboardingTokensController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{reference:guid}/raw")]
    [RateLimitExempt(
        "M2M ServiceOnly (Fase 9) — invocado por Notification vía Scribe, nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<ResolveRegistrationTokenReferenceResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRaw(Guid reference, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ResolveRegistrationTokenReferenceResponse>>(
            new ResolveRegistrationTokenReferenceQuery(reference),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
