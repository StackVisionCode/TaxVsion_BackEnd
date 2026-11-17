using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Subscriptions.Discounts;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

/// <summary>
/// Descuento de la suscripción de una oficina dado por el PlatformAdmin (retención o acuerdo), sin código. Uno vigente a
/// la vez: para cambiarlo, <c>replace = true</c>. Todo queda auditado con el actor.
/// </summary>
[ApiController]
[Route("admin/subscription/tenants/{tenantId:guid}/discount")]
[Authorize]
[HasPermission(SubscriptionPermissions.AdminCrossTenant)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class AdminSubscriptionDiscountController(IMessageBus bus) : ControllerBase
{
    public sealed record GrantDiscountRequest(
        int? PercentBasisPoints,
        long? AmountCents,
        string? Currency,
        DiscountDuration Duration,
        int? DurationMonths,
        DiscountAppliesTo AppliesTo,
        string Reason,
        bool Replace = false
    );

    public sealed record EndDiscountRequest(string Reason);

    [HttpPost]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<SubscriptionDiscountView>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Grant(Guid tenantId, GrantDiscountRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SubscriptionDiscountView>>(
            new GrantSubscriptionDiscountCommand(
                tenantId,
                request.PercentBasisPoints,
                request.AmountCents,
                request.Currency,
                request.Duration,
                request.DurationMonths,
                request.AppliesTo,
                request.Reason,
                request.Replace,
                actorId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType<SubscriptionDiscountView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SubscriptionDiscountView?>>(
            new GetSubscriptionDiscountQuery(tenantId),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);
        return result.Value is null ? NoContent() : Ok(result.Value);
    }

    [HttpDelete]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> End(Guid tenantId, [FromBody] EndDiscountRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new EndSubscriptionDiscountCommand(tenantId, request.Reason, actorId),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
