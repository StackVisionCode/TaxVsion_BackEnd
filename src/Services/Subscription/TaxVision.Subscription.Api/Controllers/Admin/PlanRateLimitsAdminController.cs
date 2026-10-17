using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.RateLimiting.Commands.UpsertPlanRateLimit;
using TaxVision.Subscription.Application.RateLimiting.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>CAT-F2.6 — Autoría de multiplicadores de rate-limit por plan — solo PlatformAdmin.
/// Un plan nuevo creado por la admin API (F2.3) no trae multiplicadores sembrados: el admin los
/// agrega vía <c>PUT /{planCode}/categories/{category}</c>. La ausencia de fila implica multiplicador 1.0
/// (semántica original de <see cref="BuildingBlocks.RateLimiting.RateLimitCategory"/>).</summary>
[ApiController]
[Route("admin/subscription/plans/{planCode}/rate-limits")]
[Authorize]
[HasPermission(SubscriptionPermissions.AdminCrossTenant)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class PlanRateLimitsAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>Lista todas las filas (planCode, category) sembradas para el plan.</summary>
    [HttpGet]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(typeof(IReadOnlyList<PlanRateLimitResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(string planCode, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<PlanRateLimitResponse>>>(
            new GetPlanRateLimitsByPlanQuery(planCode),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Upsert — actualiza la fila si existe, la crea si no. Útil para añadir multiplicadores
    /// a un plan recién creado o cambiar los ya sembrados.</summary>
    [HttpPut("categories/{category}")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Upsert(
        string planCode,
        string category,
        [FromBody] UpsertPlanRateLimitRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpsertPlanRateLimitCommand(
                planCode,
                category,
                request.MultiplierOverride,
                request.HardOverridePerMinute,
                userId
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record UpsertPlanRateLimitRequest(decimal MultiplierOverride, int? HardOverridePerMinute);
