using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.AddOns.Commands.CreateQuotaAddOn;
using TaxVision.Subscription.Application.AddOns.Commands.SetAddOnPrices;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>Autoría del catálogo de add-ons — solo PlatformAdmin.</summary>
[ApiController]
[Route("admin/subscription/addons")]
[Authorize]
[HasPermission(SubscriptionPermissions.AdminCrossTenant)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class AddOnsAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>CAT-F3.5 — Crea y publica un add-on de cuota genérico (<c>storage</c>, <c>+invitaciones</c>,
    /// etc.) con un entitlement aditivo (MergeStrategy = Sum/Max) sobre una entitlement key existente, y
    /// un opcional <c>MutexGroup</c> para exclusividad entre SKUs del mismo grupo. El pipeline runtime
    /// (snapshot + evento + consumers) ya respeta MergeStrategy sin cambios adicionales.</summary>
    [HttpPost("quota")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateQuota([FromBody] CreateQuotaAddOnRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new CreateQuotaAddOnCommand(
                request.Code,
                request.Name,
                request.Description,
                request.EntitlementKey,
                request.ValueType,
                request.EntitlementAmount,
                request.MergeStrategy,
                request.MutexGroup,
                request.MonthlyUsd,
                request.YearlyUsd,
                userId
            ),
            ct
        );
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Actualiza el precio de un add-on (afecta compras nuevas).</summary>
    [HttpPut("{addOnId:guid}/prices")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPrices(
        Guid addOnId,
        [FromBody] SetAddOnPricesRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetAddOnPricesCommand(addOnId, request.MonthlyUsd, request.YearlyUsd, userId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record SetAddOnPricesRequest(decimal MonthlyUsd, decimal YearlyUsd);

public sealed record CreateQuotaAddOnRequest(
    string Code,
    string Name,
    string Description,
    string EntitlementKey,
    string ValueType,
    string EntitlementAmount,
    string MergeStrategy,
    string? MutexGroup,
    decimal MonthlyUsd,
    decimal YearlyUsd
);
