using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
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

        if (
            userId != actorUserId
            && !await permissionsSource.HasPermissionAsync(User, UserManagementPermissions.UsersWorkView, ct)
        )
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new Error("Signature.Work.Forbidden", "Viewing another member's work requires users.work.view.")
            );

        var canViewAll = await permissionsSource.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct);
        var summary = await bus.InvokeAsync<WorkSummaryResponse>(
            new GetSignatureWorkQuery(tenantId, userId, actorUserId, canViewAll),
            ct
        );
        return Ok(summary);
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
