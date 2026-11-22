using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
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

    /// <summary>
    /// "Waiting on clients" de toda la oficina (alcance Office del dashboard): sin filtro de responsable, con el ABAC
    /// de quien mira y solo con <c>users.work.view</c>. Form requests solo con su permiso y el módulo disponible.
    /// </summary>
    [HttpGet("office/waiting")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<OfficeWaitingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> OfficeWaiting(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();

        var canViewAll = await HasAsync(CustomersPermissions.ViewAll, ct);
        var tasks = await bus.InvokeAsync<Result<IReadOnlyList<OfficeWaitingCount>>>(
            new GetOfficeWaitingQuery(tenantId, actorId, await CanViewOthersAsync(ct), canViewAll),
            ct
        );
        if (tasks.IsFailure)
            return StatusCode(tasks.Error.ToHttpStatusCode(), tasks.Error);

        // Solo después de autorizar: el bloque de form requests respeta su permiso y la disponibilidad del módulo.
        if (!await IncludesClientFormsAsync(tenantId, ct))
            return Ok(new OfficeWaitingResponse(tasks.Value));
        var forms = await bus.InvokeAsync<int>(new GetFormRequestOfficeWaitingQuery(tenantId, actorId, canViewAll), ct);
        return Ok(new OfficeWaitingResponse([.. tasks.Value, new OfficeWaitingCount(WorkKinds.FormRequests, forms)]));
    }

    /// <summary>Títulos en vivo para Activity: solo lo que quien mira puede leer; lo demás se omite.</summary>
    [HttpPost("titles")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<WorkTitlesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Titles([FromBody] WorkTitlesRequest? request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();
        var refs = WorkTitleRefs.Validate(request);
        if (refs.IsFailure)
            return StatusCode(StatusCodes.Status400BadRequest, refs.Error);

        var taskIds = WorkTitleRefs.IdsOf(refs.Value, WorkResourceTypes.Task);
        var requestIds = WorkTitleRefs.IdsOf(refs.Value, WorkResourceTypes.ClientRequest);
        var formIds = WorkTitleRefs.IdsOf(refs.Value, WorkResourceTypes.FormRequest);
        if (taskIds.Count + requestIds.Count + formIds.Count == 0)
            return Ok(new WorkTitlesResponse([]));

        var canViewAll = await HasAsync(CustomersPermissions.ViewAll, ct);
        var titles =
            taskIds.Count + requestIds.Count == 0
                ? []
                : await bus.InvokeAsync<IReadOnlyList<WorkTitle>>(
                    new GetWorkTitlesQuery(tenantId, actorId, canViewAll, taskIds, requestIds),
                    ct
                );
        var forms =
            formIds.Count > 0 && await IncludesClientFormsAsync(tenantId, ct)
                ? await bus.InvokeAsync<IReadOnlyList<WorkTitle>>(
                    new GetFormRequestWorkTitlesQuery(tenantId, actorId, canViewAll, formIds),
                    ct
                )
                : [];
        return Ok(new WorkTitlesResponse([.. titles, .. forms]));
    }

    /// <summary>"Show all" de un bloque: misma autorización, universo y orden que el top de <c>/work/{userId}</c>.</summary>
    [HttpGet("{userId:guid}/items")]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    [ProducesResponseType<PagedResult<WorkItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Items(
        Guid userId,
        [FromQuery] string? kind,
        [FromQuery] int? page,
        [FromQuery] int? size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();

        var paging = WorkPaging.Normalize(page, size);
        var canViewOthers = await CanViewOthersAsync(ct);
        var canViewAll = await HasAsync(CustomersPermissions.ViewAll, ct);
        if (kind != WorkKinds.FormRequests)
        {
            var result = await bus.InvokeAsync<Result<PagedResult<WorkItem>>>(
                new GetWorkItemsQuery(
                    tenantId,
                    actorId,
                    userId,
                    canViewOthers,
                    canViewAll,
                    kind,
                    paging.Page,
                    paging.Size
                ),
                ct
            );
            return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
        }

        if (!WorkAccess.CanView(actorId, userId, canViewOthers))
            return StatusCode(StatusCodes.Status403Forbidden, WorkErrors.Forbidden);
        if (!await IncludesClientFormsAsync(tenantId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, WorkErrors.FormRequestsUnavailable);
        return Ok(
            await bus.InvokeAsync<PagedResult<WorkItem>>(
                new GetFormRequestWorkItemsQuery(tenantId, actorId, userId, canViewAll, paging.Page, paging.Size),
                ct
            )
        );
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
