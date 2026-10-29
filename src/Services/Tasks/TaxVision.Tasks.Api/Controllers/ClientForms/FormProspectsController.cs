using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Permissions;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.Api.Requests.ClientForms;
using TaxVision.Tasks.ClientForms.Application.Participants.Prospects;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers.ClientForms;

// Prospectos: participantes externos del tenant. Mismo ABAC que la bandeja para las requests externas.
[ApiController]
[ClientFormsEndpoint]
[Route("tasks/forms/prospects")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class FormProspectsController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    [HttpGet]
    [HasPermission(TasksPermissions.FormRequestsRead)]
    [RateLimit("task.f.form_read")]
    public async Task<IActionResult> List(
        [FromQuery] ProspectStatus? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int size = ListProspectsHandler.DefaultSize,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        var result = await bus.InvokeAsync<ProspectPageResponse>(
            new ListProspectsQuery(
                tenantId,
                userId,
                await CanViewAllAsync(ct),
                await CanStartCustomerCreationAsync(ct),
                status,
                search,
                page,
                size
            ),
            ct
        );
        return Ok(result);
    }

    [HttpPost("{participantId:guid}/lost")]
    [HasPermission(TasksPermissions.FormRequestsSendExternal)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> MarkLost(
        Guid participantId,
        MarkProspectLostRequest? request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<ProspectResponse>>(
                new MarkProspectLostCommand(
                    tenantId,
                    userId,
                    await CanViewAllAsync(ct),
                    await CanStartCustomerCreationAsync(ct),
                    participantId,
                    request?.Reason,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    [HttpPost("{participantId:guid}/reopen")]
    [HasPermission(TasksPermissions.FormRequestsSendExternal)]
    [RateLimit("task.h.form_requests_write")]
    public async Task<IActionResult> Reopen(Guid participantId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();
        return ToActionResult(
            await bus.InvokeAsync<Result<ProspectResponse>>(
                new ReopenProspectCommand(
                    tenantId,
                    userId,
                    await CanViewAllAsync(ct),
                    await CanStartCustomerCreationAsync(ct),
                    participantId,
                    DateTime.UtcNow
                ),
                ct
            )
        );
    }

    private Task<bool> CanViewAllAsync(CancellationToken ct) =>
        permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);

    // Mismos permisos que POST requests/{id}/actions/create-customer.
    private async Task<bool> CanStartCustomerCreationAsync(CancellationToken ct) =>
        await permissions.HasPermissionAsync(User, TasksPermissions.FormRequestsReview, ct)
        && await permissions.HasPermissionAsync(User, CustomersPermissions.Manage, ct);

    private IActionResult ToActionResult<T>(Result<T> result) =>
        result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
}
