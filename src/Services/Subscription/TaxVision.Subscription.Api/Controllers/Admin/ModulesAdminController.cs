using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Catalog.Commands.CreateModule;
using TaxVision.Subscription.Application.Catalog.Commands.UpdateModule;
using TaxVision.Subscription.Application.Catalog.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers.Admin;

[ApiController]
[Route("admin/subscription/modules")]
[Authorize]
[HasPermission(SubscriptionPermissions.AdminCrossTenant)]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class ModulesAdminController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(typeof(IReadOnlyList<ModuleAdminResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<ModuleAdminResponse>>>(
            new GetModulesForAdminQuery(),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{code}")]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(string code, [FromBody] UpdateModuleRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpdateModuleCommand(
                code,
                request.Category,
                request.AddonPriceMonthlyUsd,
                request.AddonPriceYearlyUsd,
                userId
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateModuleRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new CreateModuleCommand(
                request.Code,
                request.Name,
                request.Description,
                request.Category,
                request.AddonPriceMonthlyUsd,
                request.AddonPriceYearlyUsd,
                userId
            ),
            ct
        );
        return result.IsSuccess
            ? Created($"/admin/subscription/modules/{request.Code.Trim().ToLowerInvariant()}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record UpdateModuleRequest(string Category, decimal? AddonPriceMonthlyUsd, decimal? AddonPriceYearlyUsd);

public sealed record CreateModuleRequest(
    string Code,
    string Name,
    string Description,
    string Category,
    decimal? AddonPriceMonthlyUsd,
    decimal? AddonPriceYearlyUsd
);
