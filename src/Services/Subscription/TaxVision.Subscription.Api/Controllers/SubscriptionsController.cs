using TaxVision.Subscription.Domain.Plans;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Subscriptions.Commands;
using TaxVision.Subscription.Application.Subscriptions.Dtos;
using TaxVision.Subscription.Domain.ValueObjects;
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

    public sealed record AddSeatRequest(int Quantity);
    public sealed record CancelSubscriptionRequest(string? Reason);
    public sealed record ChangePlanRequestBody(Guid? NewPlanId, BillingPeriod? NewBillingPeriod, string? GiftCardCode);

    // ─── EXISTING SEAT/CANCEL ENDPOINTS ───────────────────────────────────────

    /// <summary>Buy additional seats. Returns SeatId in PendingPayment state.</summary>
    [HttpPost("current/seats")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> AddSeat([FromBody] AddSeatRequest req, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<AddSeatResponse>>(
            new AddSeatCommand(CurrentTenantId, req.Quantity), ct);

        return result.IsSuccess
            ? Accepted(new
            {
                result.Value.SeatId,
                result.Value.Status,
                result.Value.Quantity,
                result.Value.TotalAmount,
                result.Value.Currency,
                result.Value.BillingAnchorDay,
                result.Value.PeriodEndUtc
            })
            : UnprocessableEntity(new { result.Error.Code, result.Error.Message });
    }

    /// <summary>Cancel the subscription at the end of the current period.</summary>
    [HttpPost("current/cancel")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> Cancel([FromBody] CancelSubscriptionRequest req, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(
            new CancelAtPeriodEndCommand(CurrentTenantId), ct);

        return result.IsSuccess
            ? Ok(new { message = "Subscription will be cancelled at the end of the current period." })
            : UnprocessableEntity(new { result.Error.Code, result.Error.Message });
    }

    // ─── NEW SUBSCRIPTION MANAGEMENT ENDPOINTS ────────────────────────────────

    /// <summary>Create a subscription for a tenant (Developer only).</summary>
    [HttpPost]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequest req, CancellationToken ct)
    {
        var cmd = new CreateSubscriptionCommand(
            TenantId: req.TenantId,
            ServiceLevel: req.ServiceLevel,
            BillingPeriod: req.BillingPeriod,
            IsActive: req.IsActive,
            StartDate: req.StartDate);

        var result = await bus.InvokeAsync<CreateSubscriptionResponse>(cmd, ct);
        return Ok(result);
    }

    /// <summary>Renew the current tenant's subscription (Developer or system).</summary>
    [HttpPost("current/renew")]
    [Authorize(Roles = "PlatformAdmin,TenantAdmin")]
    public async Task<IActionResult> Renew(Guid subscriptionId, CancellationToken ct)
    {
        await bus.InvokeAsync<bool>(new RenewSubscriptionCommand(subscriptionId, false), ct);
        return Ok(new { message = "Subscription renewed successfully." });
    }

    /// <summary>Initiate a plan change (upgrade/downgrade/billing period). Returns payment link info.</summary>
    [HttpPost("{subscriptionId:guid}/change-plan")]
    [Authorize(Roles = "TenantAdmin")]
    public async Task<IActionResult> ChangePlan(
        Guid subscriptionId,
        [FromBody] ChangePlanRequestBody req,
        CancellationToken ct)
    {
        var cmd = new ChangePlanCommand(
            SubscriptionId: subscriptionId,
            NewPlanId: req.NewPlanId,
            NewBillingPeriod: req.NewBillingPeriod);

        var result = await bus.InvokeAsync<ChangePlanResponse>(cmd, ct);
        return Ok(result);
    }

    /// <summary>Apply a pending plan change after payment confirmation (Developer/system only).</summary>
    [HttpPost("pending-changes/{pendingChangeId:guid}/apply")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> ApplyPendingChange(Guid pendingChangeId, CancellationToken ct)
    {
        await bus.InvokeAsync<bool>(new ApplyPendingChangeCommand(pendingChangeId), ct);
        return Ok(new { message = "Pending plan change applied successfully." });
    }

    /// <summary>Update the subscription price (Developer only).</summary>
    [HttpPatch("{subscriptionId:guid}/price")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> UpdatePrice(
        Guid subscriptionId,
        [FromBody] UpdatePriceRequest req,
        CancellationToken ct)
    {
        await bus.InvokeAsync<bool>(new UpdateSubscriptionPriceCommand(subscriptionId, req.NewPrice), ct);
        return Ok(new { message = "Price updated successfully." });
    }

    public sealed record CreateSubscriptionRequest(
        Guid TenantId,
        ServiceLevel ServiceLevel,
        BillingPeriod BillingPeriod,
        bool IsActive = true,
        DateTime? StartDate = null);

    public sealed record UpdatePriceRequest(decimal NewPrice);
}
