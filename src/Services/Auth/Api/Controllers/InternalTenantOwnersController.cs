using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.Onboarding.Internal.Commands;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>PayFlow (Fase 16) — M2M-only: la Saga de onboarding (Fase 15) invoca este endpoint para
/// crear el TenantAdmin de un tenant recién provisionado. El password nunca viaja en el body — solo
/// una referencia de un solo uso a un hash ya calculado (ver doc-comment del handler).</summary>
[ApiController]
[Route("auth/internal/tenants/{tenantId:guid}/owners")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalTenantOwnersController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateOwnerRequest(
        Guid OnboardingId,
        string Email,
        string FirstName,
        string LastName,
        Guid PasswordHashReference
    );

    [HttpPost]
    [RateLimitExempt(
        "M2M ServiceOnly (Fase 16) — invocado por la Saga de onboarding, nunca expuesto al Gateway público."
    )]
    public async Task<IActionResult> Create(Guid tenantId, [FromBody] CreateOwnerRequest request, CancellationToken ct)
    {
        var command = new CreateTenantOwnerFromOnboardingCommand(
            request.OnboardingId,
            tenantId,
            request.Email,
            request.FirstName,
            request.LastName,
            request.PasswordHashReference
        );

        var result = await bus.InvokeAsync<Result>(command, ct);
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
