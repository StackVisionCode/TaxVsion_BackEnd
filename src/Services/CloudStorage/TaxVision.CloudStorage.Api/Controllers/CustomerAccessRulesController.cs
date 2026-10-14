using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.CustomerAccess;
using TaxVision.CloudStorage.Application.CustomerAccess.Commands.ClearDenyOverride;
using TaxVision.CloudStorage.Application.CustomerAccess.Commands.SetDenyOverride;
using TaxVision.CloudStorage.Application.CustomerAccess.Queries.GetRulesByCustomer;
using TaxVision.CloudStorage.Domain.CustomerAccess;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Endpoints admin para declarar reglas deny per-item sobre un cliente. El gate del endpoint se abre
/// con cualquiera de los dos permisos de delegación; la decisión granular (asignación obligatoria o
/// bypass admin) la toma el handler.
/// </summary>
[ApiController]
[Route("storage/customer-access")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class CustomerAccessRulesController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    public sealed record SetDenyRuleRequest(
        AccessResourceType ResourceType,
        Guid ResourceId,
        AccessCapability Capability,
        string? Reason
    );

    [HttpPost("{customerId:guid}/rules")]
    [RateLimit("cloudstorage.g.folder_manage")]
    [ProducesResponseType<SetDenyOverrideResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(Guid customerId, SetDenyRuleRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();
        var (hasAny, hasAssigned) = await ResolveDelegationPermissionsAsync(ct);
        if (!hasAny && !hasAssigned)
            return Forbid();

        var result = await bus.InvokeAsync<Result<SetDenyOverrideResponse>>(
            new SetDenyOverrideCommand(
                tenantId,
                actorId,
                customerId,
                request.ResourceType,
                request.ResourceId,
                request.Capability,
                request.Reason,
                hasAny,
                hasAssigned
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{customerId:guid}/rules")]
    [RateLimit("cloudstorage.g.folder_manage")]
    [ProducesResponseType<ClearDenyOverrideResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Clear(
        Guid customerId,
        [FromQuery] AccessResourceType resourceType,
        [FromQuery] Guid resourceId,
        [FromQuery] AccessCapability capability,
        CancellationToken ct
    )
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();
        var (hasAny, hasAssigned) = await ResolveDelegationPermissionsAsync(ct);
        if (!hasAny && !hasAssigned)
            return Forbid();

        var result = await bus.InvokeAsync<Result<ClearDenyOverrideResponse>>(
            new ClearDenyOverrideCommand(
                tenantId,
                actorId,
                customerId,
                resourceType,
                resourceId,
                capability,
                hasAny,
                hasAssigned
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{customerId:guid}/rules")]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<IReadOnlyList<CustomerAccessRuleView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid customerId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();
        var (hasAny, hasAssigned) = await ResolveDelegationPermissionsAsync(ct);
        if (!hasAny && !hasAssigned)
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<CustomerAccessRuleView>>>(
            new GetRulesByCustomerQuery(tenantId, actorId, customerId, hasAny, hasAssigned),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private async Task<(bool HasAny, bool HasAssigned)> ResolveDelegationPermissionsAsync(CancellationToken ct)
    {
        var hasAny = await permissions.HasPermissionAsync(User, CloudStoragePermissions.CustomerAccessManageAny, ct);
        var hasAssigned = await permissions.HasPermissionAsync(
            User,
            CloudStoragePermissions.CustomerAccessManageAssigned,
            ct
        );
        return (hasAny, hasAssigned);
    }
}
