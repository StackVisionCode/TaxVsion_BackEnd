using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.SubscriptionModules.Dtos;
using TaxVision.Subscription.Application.SubscriptionModules.Commands;
using TaxVision.Subscription.Application.SubscriptionModules.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("api/subscription-modules")]
[Authorize]
public sealed class SubscriptionModulesController(IMessageBus bus) : ControllerBase
{
    private Guid CurrentTenantId =>
        Guid.Parse(User.FindFirst("tenant_id")?.Value
            ?? throw new InvalidOperationException("tenant_id claim is missing."));

    /// <summary>Get all modules for a subscription.</summary>
    [HttpGet("subscription/{subscriptionId:guid}")]
    public async Task<IActionResult> GetBySubscription(
        Guid subscriptionId, [FromQuery] bool? isIncluded, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<List<SubscriptionModuleDto>>(
            new GetSubscriptionModulesQuery(subscriptionId, isIncluded), ct);
        return Ok(result);
    }

    /// <summary>Assign a module to a subscription (Developer only).</summary>
    [HttpPost]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> Assign([FromBody] AssignModuleRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<SubscriptionModuleDto>(
            new AssignSubscriptionModuleCommand(request.SubscriptionId, request.ModuleId, request.IsIncluded), ct);
        return Ok(result);
    }

    /// <summary>Remove a module assignment (Developer only).</summary>
    [HttpDelete("{subscriptionModuleId:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    public async Task<IActionResult> Remove(Guid subscriptionModuleId, CancellationToken ct)
    {
        await bus.InvokeAsync<bool>(new RemoveSubscriptionModuleCommand(subscriptionModuleId), ct);
        return NoContent();
    }
}
