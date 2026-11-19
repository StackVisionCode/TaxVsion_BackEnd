using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Application.Work;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Work;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// Proveedor de trabajo del Work Profile: lo que una persona tiene a cargo en Tasks y Client Forms, contado con el ABAC
/// de quien mira. El propio usuario siempre; otra persona y el lote exigen además <c>users.work.view</c>.
/// </summary>
[ApiController]
[Route("tasks/work")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class WorkController(
    IMessageBus bus,
    IUserPermissionsSource permissions,
    IClientFormsAvailabilityEvaluator clientForms
) : ControllerBase
{
    [HttpGet("{userId:guid}")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<WorkSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Work(Guid userId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();

        var canViewAll = await HasAsync(CustomersPermissions.ViewAll, ct);
        var now = DateTime.UtcNow;
        var tasks = await bus.InvokeAsync<Result<TaskWork>>(
            new GetUserWorkQuery(tenantId, actorId, userId, await CanViewOthersAsync(ct), canViewAll, now),
            ct
        );
        if (tasks.IsFailure)
            return StatusCode(tasks.Error.ToHttpStatusCode(), tasks.Error);

        // Solo después de autorizar: el bloque de form requests respeta su permiso y la disponibilidad del módulo.
        var forms = await IncludesClientFormsAsync(tenantId, ct)
            ? await bus.InvokeAsync<FormRequestWork>(
                new GetFormRequestWorkQuery(tenantId, actorId, userId, canViewAll, now),
                ct
            )
            : null;
        return Ok(WorkSummaryComposer.Compose(tasks.Value, forms?.Count, forms?.Top ?? []));
    }

    [HttpGet("summary")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<WorkBatchResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] string? userIds, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();
        if (!TryParseUserIds(userIds, out var ids))
            return BadRequest(new Error("Task.Work.InvalidUserIds", "userIds must be a comma-separated list of ids."));

        var canViewAll = await HasAsync(CustomersPermissions.ViewAll, ct);
        var now = DateTime.UtcNow;
        var tasks = await bus.InvokeAsync<Result<IReadOnlyList<WorkBatchRow>>>(
            new GetWorkBatchQuery(tenantId, actorId, ids, await CanViewOthersAsync(ct), canViewAll, now),
            ct
        );
        if (tasks.IsFailure)
            return StatusCode(tasks.Error.ToHttpStatusCode(), tasks.Error);

        var forms = await IncludesClientFormsAsync(tenantId, ct)
            ? await bus.InvokeAsync<IReadOnlyList<WorkBatchRow>>(
                new GetFormRequestWorkBatchQuery(tenantId, actorId, ids.Distinct().ToList(), canViewAll, now),
                ct
            )
            : [];
        return Ok(WorkSummaryComposer.Combine(ids, tasks.Value, forms));
    }

    private Task<bool> CanViewOthersAsync(CancellationToken ct) =>
        HasAsync(UserManagementPermissions.UsersWorkView, ct);

    private async Task<bool> IncludesClientFormsAsync(Guid tenantId, CancellationToken ct) =>
        await HasAsync(TasksPermissions.FormRequestsRead, ct)
        && (await clientForms.EvaluateAsync(tenantId, ct)).Enabled;

    private Task<bool> HasAsync(string permission, CancellationToken ct) =>
        permissions.HasPermissionAsync(User, permission, ct);

    private static bool TryParseUserIds(string? raw, out IReadOnlyList<Guid> ids)
    {
        var parsed = new List<Guid>();
        ids = parsed;
        foreach (var part in (raw ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Guid.TryParse(part.Trim(), out var id))
                return false;
            parsed.Add(id);
        }
        return true;
    }
}
