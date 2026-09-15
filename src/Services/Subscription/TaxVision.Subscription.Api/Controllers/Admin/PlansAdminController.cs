using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanModules;
using TaxVision.Subscription.Application.Plans.Commands.SetPlanPrices;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>Autoría de planes — solo PlatformAdmin.</summary>
[ApiController]
[Route("admin/subscription/plans")]
[Authorize]
[HasPermission(SubscriptionPermissions.AdminCrossTenant)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class PlansAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>Fija los módulos (module.*) del plan: publica una versión nueva (supersede la anterior)
    /// y encola el recálculo masivo de sus tenants.</summary>
    [HttpPut("{planId:guid}/modules")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SetModules(
        Guid planId,
        [FromBody] SetPlanModulesRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetPlanModulesCommand(planId, request.Modules ?? [], userId),
            ct
        );
        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Revisa el precio mensual/anual del plan (publica una versión nueva que supersede la anterior).
    /// No cambia entitlements, así que no dispara recálculo.</summary>
    [HttpPut("{planId:guid}/prices")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPrices(
        Guid planId,
        [FromBody] SetPlanPricesRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetPlanPricesCommand(planId, request.MonthlyUsd, request.YearlyUsd, userId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record SetPlanModulesRequest(IReadOnlyList<string> Modules);

public sealed record SetPlanPricesRequest(decimal MonthlyUsd, decimal YearlyUsd);
