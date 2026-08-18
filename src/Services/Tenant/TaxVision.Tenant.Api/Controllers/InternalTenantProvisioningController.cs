using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Tenants.Commands;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>PayFlow (Fase 16) — M2M-only: la Saga de onboarding de Auth (Fase 15) invoca este
/// endpoint para crear el Tenant real tras la confirmación de pago.</summary>
[ApiController]
[Route("internal/tenants")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalTenantProvisioningController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateFromOnboardingRequest(
        Guid OnboardingId,
        string OfficeName,
        string Subdomain,
        string AdminEmail,
        DateTime PaymentCompletedAtUtc
    );

    [HttpPost("from-onboarding")]
    [RateLimitExempt("M2M interno (actor_type=Service), nunca expuesto en el Gateway público.")]
    public async Task<IActionResult> CreateFromOnboarding(
        [FromBody] CreateFromOnboardingRequest request,
        CancellationToken ct
    )
    {
        var command = new CreateTenantFromOnboardingCommand(
            request.OnboardingId,
            request.OfficeName,
            request.Subdomain,
            request.AdminEmail,
            request.PaymentCompletedAtUtc
        );

        var result = await bus.InvokeAsync<Result>(command, ct);
        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
