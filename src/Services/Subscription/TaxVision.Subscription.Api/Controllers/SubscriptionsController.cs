using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Subscriptions.Commands;
using TaxVision.Subscription.Application.Subscriptions.Dtos;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("subscriptions")]
[Authorize]
public sealed class SubscriptionsController(IMessageBus bus) : ControllerBase
{
    private Guid CurrentTenantId =>
        Guid.Parse(User.FindFirst("tenant_id")?.Value
            ?? throw new InvalidOperationException("tenant_id claim is missing."));

    [HttpPost]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<CreateSubscriptionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSubscriptionCommand command,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<CreateSubscriptionResponse>>(command, ct);

        return result.IsSuccess
            ? Created($"/subscriptions/{result.Value.SubscriptionId}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("current/seats")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<AddSeatResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddSeat(
        [FromBody] AddSeatRequest request,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AddSeatResponse>>(
            new AddSeatCommand(CurrentTenantId, request.Quantity), ct);

        return result.IsSuccess
            ? Accepted(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("current/cancel")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(
            new CancelAtPeriodEndCommand(CurrentTenantId), ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("current/renew")]
    [Authorize(Roles = "PlatformAdmin,TenantAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Renew([FromQuery] Guid subscriptionId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(
            new RenewSubscriptionCommand(subscriptionId, false), ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{subscriptionId:guid}/change-plan")]
    [Authorize(Roles = "TenantAdmin")]
    [ProducesResponseType<ChangePlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePlan(
        Guid subscriptionId,
        [FromBody] ChangePlanRequest request,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ChangePlanResponse>>(
            new ChangePlanCommand(subscriptionId, request.NewPlanId, request.NewBillingPeriod), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("pending-changes/{pendingChangeId:guid}/apply")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplyPendingChange(Guid pendingChangeId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(
            new ApplyPendingChangeCommand(pendingChangeId), ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPatch("{subscriptionId:guid}/price")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePrice(
        Guid subscriptionId,
        [FromBody] UpdatePriceRequest request,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(
            new UpdateSubscriptionPriceCommand(subscriptionId, request.NewPrice), ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
