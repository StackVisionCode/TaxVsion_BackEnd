using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.SubscriptionModules.Commands;
using TaxVision.Subscription.Application.SubscriptionModules.Dtos;
using TaxVision.Subscription.Application.SubscriptionModules.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

[ApiController]
[Route("api/subscription-modules")]
[Authorize]
public sealed class SubscriptionModulesController(IMessageBus bus) : ControllerBase
{
    [HttpGet("subscription/{subscriptionId:guid}")]
    [ProducesResponseType<List<SubscriptionModuleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBySubscription(
        Guid subscriptionId,
        [FromQuery] bool? isIncluded,
        CancellationToken ct)
    {
        var result = await bus.InvokeAsync<List<SubscriptionModuleDto>>(
            new GetSubscriptionModulesQuery(subscriptionId, isIncluded), ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<SubscriptionModuleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Assign([FromBody] AssignModuleRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<SubscriptionModuleDto>>(
            new AssignSubscriptionModuleCommand(request.SubscriptionId, request.ModuleId, request.IsIncluded), ct);

        return result.IsSuccess
            ? Created($"/api/subscription-modules/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{subscriptionModuleId:guid}")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid subscriptionModuleId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result>(
            new RemoveSubscriptionModuleCommand(subscriptionModuleId), ct);

        return result.IsSuccess
            ? NoContent()
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
