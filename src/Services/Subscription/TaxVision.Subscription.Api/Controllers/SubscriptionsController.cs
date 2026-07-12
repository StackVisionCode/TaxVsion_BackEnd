using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Application.Subscriptions.Commands.Reactivate;
using TaxVision.Subscription.Application.Subscriptions.Commands.Suspend;
using TaxVision.Subscription.Application.Subscriptions.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("subscriptions")]
[Authorize]
public sealed class SubscriptionsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Suscripción base del tenant autenticado (plan, límites, renovación, estado).
    /// Los asientos (seats) se consultan por separado — ver /seats.</summary>
    [HttpGet("me")]
    [ProducesResponseType<MySubscriptionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMySubscription(CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<MySubscriptionResponse>>(new GetMySubscriptionQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ChangePlanRequest(string PlanCode);

    [HttpPost("change-plan")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePlan(ChangePlanRequest request, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ChangePlanCommand(tenantId, request.PlanCode, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CancelRequest(string Reason);

    [HttpPost("cancel")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(CancelRequest request, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelSubscriptionCommand(tenantId, request.Reason, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record SuspendRequest(string Reason);

    /// <summary>Suspensión administrativa (impago, violación de políticas). Solo plataforma.</summary>
    [HttpPatch("{tenantId:guid}/suspend")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Suspend(Guid tenantId, SuspendRequest request, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new SuspendSubscriptionCommand(tenantId, request.Reason, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPatch("{tenantId:guid}/reactivate")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid tenantId, CancellationToken ct)
    {
        if (!TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ReactivateSubscriptionCommand(tenantId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private bool TryGetTenantAndUser(out Guid tenantId, out Guid userId)
    {
        userId = Guid.Empty;
        if (!Guid.TryParse(User.FindFirst("tenant_id")?.Value, out tenantId))
            return false;

        var raw =
            User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }
}
