using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Entitlements.Overrides;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>
/// Plan a la medida: el PlatformAdmin fija para UNA oficina el valor de un límite (almacenamiento, usuarios,
/// módulos vendibles, reuniones…) encima de su plan. No cambia lo que se cobra. Cada cambio pide motivo, queda
/// auditado con el actor y recalcula la oficina; quitarlo la devuelve a su plan.
/// </summary>
[ApiController]
[Route("admin/subscription/tenants/{tenantId:guid}/overrides")]
[Authorize]
[HasPermission(SubscriptionPermissions.AdminCrossTenant)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class AdminEntitlementOverridesController(IMessageBus bus) : ControllerBase
{
    /// <summary><c>expiresAtUtc</c> null = hasta que se quite a mano.</summary>
    public sealed record SetOverrideRequest(string Value, string Reason, DateTime? ExpiresAtUtc);

    public sealed record RemoveOverrideRequest(string Reason);

    [HttpGet]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<IReadOnlyList<TenantEntitlementOverrideView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid tenantId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<TenantEntitlementOverrideView>>>(
            new GetTenantEntitlementOverridesQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{key}")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<TenantEntitlementOverrideView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Set(Guid tenantId, string key, SetOverrideRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TenantEntitlementOverrideView>>(
            new SetTenantEntitlementOverrideCommand(
                tenantId,
                key,
                request.Value,
                request.Reason,
                request.ExpiresAtUtc,
                actorId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{key}")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(
        Guid tenantId,
        string key,
        [FromBody] RemoveOverrideRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out _, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RemoveTenantEntitlementOverrideCommand(tenantId, key, request.Reason, actorId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
