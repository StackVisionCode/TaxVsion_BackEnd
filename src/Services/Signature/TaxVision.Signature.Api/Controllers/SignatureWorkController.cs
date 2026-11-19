using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Signature.Application.Requests.Queries.Work;
using Wolverine;

namespace TaxVision.Signature.Api.Controllers;

/// <summary>
/// Proveedor "work" del Work Profile: firmas a cargo de una persona. El propio trabajo con la lectura del módulo;
/// el de otra persona y el lote exigen además <c>users.work.view</c>. Conteos y top con el ABAC de quien mira.
/// </summary>
[ApiController]
[Route("signature/work")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SignatureWorkController(IMessageBus bus, IUserPermissionsSource permissionsSource) : ControllerBase
{
    // ---------- GET /signature/work/{userId} ----------
    [HttpGet("{userId:guid}")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.request_read")]
    [ProducesResponseType<WorkSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(Guid userId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorUserId))
            return Unauthorized();

        if (!await CanViewMemberAsync(userId, actorUserId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, MemberForbidden);

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var summary = await bus.InvokeAsync<WorkSummaryResponse>(
            new GetSignatureWorkQuery(tenantId, userId, actorUserId, canViewAll),
            ct
        );
        return Ok(summary);
    }

    // ---------- GET /signature/work/{userId}/items?kind=signatures&page=&size= ----------
    [HttpGet("{userId:guid}/items")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.request_read")]
    [ProducesResponseType<PagedResult<WorkItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Items(
        Guid userId,
        [FromQuery] string? kind,
        [FromQuery] int? page,
        [FromQuery] int? size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorUserId))
            return Unauthorized();

        if (!await CanViewMemberAsync(userId, actorUserId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, MemberForbidden);

        var paging = ParseItemsPage(kind, page, size);
        if (paging.IsFailure)
            return StatusCode(StatusCodes.Status400BadRequest, paging.Error);

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var items = await bus.InvokeAsync<PagedResult<WorkItem>>(
            new GetSignatureWorkItemsQuery(
                tenantId,
                userId,
                actorUserId,
                canViewAll,
                paging.Value.Page,
                paging.Value.Size
            ),
            ct
        );
        return Ok(items);
    }

    // ---------- POST /signature/work/titles ----------
    [HttpPost("titles")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [RateLimit("signature.f.request_read")]
    [ProducesResponseType<WorkTitlesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Titles([FromBody] WorkTitlesRequest? request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorUserId))
            return Unauthorized();

        var ids = ParseTitleRefs(request);
        if (ids.IsFailure)
            return StatusCode(StatusCodes.Status400BadRequest, ids.Error);
        if (ids.Value.Count == 0)
            return Ok(new WorkTitlesResponse([]));

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var titles = await bus.InvokeAsync<WorkTitlesResponse>(
            new GetSignatureWorkTitlesQuery(tenantId, actorUserId, canViewAll, ids.Value),
            ct
        );
        return Ok(titles);
    }

    private static readonly Error MemberForbidden = new(
        "Signature.Work.Forbidden",
        "Viewing another member's work requires users.work.view."
    );

    // El propio trabajo siempre; el de otra persona exige users.work.view.
    private async Task<bool> CanViewMemberAsync(Guid userId, Guid actorUserId, CancellationToken ct) =>
        userId == actorUserId
        || await permissionsSource.HasPermissionAsync(User, UserManagementPermissions.UsersWorkView, ct);

    // Solo el tipo "signatures"; page desde 1 y size entre 1 y WorkLimits.ItemsMaxPageSize.
    private static Result<(int Page, int Size)> ParseItemsPage(string? kind, int? page, int? size)
    {
        if (!string.Equals(kind, WorkKinds.Signatures, StringComparison.Ordinal))
            return Result.Failure<(int, int)>(
                new Error("Signature.Work.Kind", $"kind must be '{WorkKinds.Signatures}'.")
            );

        var (pageValue, sizeValue) = (page ?? 1, size ?? WorkLimits.ItemsPageSize);
        if (pageValue < 1 || sizeValue is < 1 or > WorkLimits.ItemsMaxPageSize)
            return Result.Failure<(int, int)>(
                new Error(
                    "Signature.Work.Paging",
                    $"page must be at least 1 and size between 1 and {WorkLimits.ItemsMaxPageSize}."
                )
            );
        return Result.Success((pageValue, sizeValue));
    }

    // Ids de solicitudes sin repetidos; los tipos ajenos se ignoran. Más de WorkLimits.TitleBatch refs → error.
    private static Result<IReadOnlyList<Guid>> ParseTitleRefs(WorkTitlesRequest? request)
    {
        if (request?.Items is not { } refs)
            return Result.Failure<IReadOnlyList<Guid>>(new Error("Signature.Work.Titles", "items is required."));
        if (refs.Count > WorkLimits.TitleBatch)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("Signature.Work.Titles", $"items must have at most {WorkLimits.TitleBatch} refs.")
            );

        IReadOnlyList<Guid> ids = refs.Where(item =>
                item is not null
                && string.Equals(item.ResourceType, WorkResourceTypes.SignatureRequest, StringComparison.Ordinal)
            )
            .Select(item => item.Id)
            .Distinct()
            .ToList();
        return Result.Success(ids);
    }

    // ---------- GET /signature/work/summary?userIds=a,b,c ----------
    [HttpGet("summary")]
    [HasPermission(SignaturePermissions.RequestRead)]
    [HasPermission(UserManagementPermissions.UsersWorkView)]
    [RateLimit("signature.f.request_read")]
    [ProducesResponseType<WorkBatchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Summary([FromQuery] string? userIds, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorUserId))
            return Unauthorized();

        var parsed = ParseUserIds(userIds);
        if (parsed.IsFailure)
            return StatusCode(StatusCodes.Status400BadRequest, parsed.Error);

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var batch = await bus.InvokeAsync<WorkBatchResponse>(
            new GetSignatureWorkBatchQuery(tenantId, parsed.Value, actorUserId, canViewAll),
            ct
        );
        return Ok(batch);
    }

    // Lista separada por comas, sin repetidos, entre 1 y WorkLimits.BatchUsers.
    private static Result<IReadOnlyList<Guid>> ParseUserIds(string? raw)
    {
        var parts = (raw ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        var ids = new List<Guid>(parts.Length);
        foreach (var part in parts)
        {
            if (!Guid.TryParse(part, out var id))
                return Result.Failure<IReadOnlyList<Guid>>(
                    new Error("Signature.Work.UserIds", "userIds must be a comma-separated list of ids.")
                );
            if (!ids.Contains(id))
                ids.Add(id);
        }

        if (ids.Count is 0 or > WorkLimits.BatchUsers)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("Signature.Work.UserIds", $"userIds must have between 1 and {WorkLimits.BatchUsers} ids.")
            );
        return Result.Success<IReadOnlyList<Guid>>(ids);
    }
}
