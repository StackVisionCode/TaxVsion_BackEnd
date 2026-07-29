using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Onboarding.Registration.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow (Fase 16) — M2M-only: Tenant lo consulta antes de crear un Tenant real a partir
/// de un onboarding, para confirmar que el pago está confirmado y el onboarding sigue en curso.</summary>
[ApiController]
[Route("auth/internal/onboarding")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalOnboardingStatusController(IMessageBus bus) : ControllerBase
{
    [HttpGet("{onboardingId:guid}/status")]
    [ProducesResponseType<InternalOnboardingStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(Guid onboardingId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<InternalOnboardingStatusResponse>>(
            new GetInternalOnboardingStatusQuery(onboardingId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
