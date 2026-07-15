using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Fase C3 — nucleo autenticado de compartir archivos. La resolucion del token en
/// si (publico/privado) vive en PublicShareController/PrivateShareController —
/// esta clase solo gestiona el ciclo de vida (crear/listar/revocar/expiracion).
/// </summary>
[ApiController]
[Route("storage")]
[Authorize]
public sealed class ShareLinksController(IMessageBus bus, ICorrelationContext correlation) : ControllerBase
{
    public sealed record CreateShareLinkRequest(
        ShareVisibility Visibility,
        SharePermission Permission,
        string? Password,
        DateTime? ExpiresAtUtc,
        int? MaxAccessCount,
        IReadOnlyList<Guid>? RecipientUserIds,
        IReadOnlyList<Guid>? RecipientCustomerIds,
        IReadOnlyList<string>? RecipientEmails
    );

    [HttpPost("files/{fileId:guid}/shares")]
    [Authorize(Policy = CloudStoragePermissions.ShareCreate)]
    [ProducesResponseType<CreatedShareLinkResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid fileId, CreateShareLinkRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreatedShareLinkResponse>>(
            new CreateShareLinkCommand(
                tenantId,
                actorId,
                scope,
                User.HasClaim("perm", CloudStoragePermissions.ShareManage),
                fileId,
                request.Visibility,
                request.Permission,
                request.Password,
                request.ExpiresAtUtc,
                request.MaxAccessCount,
                request.RecipientUserIds ?? [],
                request.RecipientCustomerIds ?? [],
                request.RecipientEmails ?? [],
                AuditContext()
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(ListForFile), new { fileId }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("files/{fileId:guid}/shares")]
    [Authorize(Policy = CloudStoragePermissions.FileView)]
    [ProducesResponseType<IReadOnlyList<ShareLinkResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForFile(Guid fileId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out _, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<ShareLinkResponse>>>(
            new ListShareLinksForFileQuery(tenantId, scope, fileId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Fase C4 — IsRecursive/AppliesToFutureItems solo tienen efecto aca (ver FolderShareCoverage).</summary>
    public sealed record CreateFolderShareLinkRequest(
        ShareVisibility Visibility,
        SharePermission Permission,
        string? Password,
        DateTime? ExpiresAtUtc,
        int? MaxAccessCount,
        bool IsRecursive,
        bool AppliesToFutureItems,
        IReadOnlyList<Guid>? RecipientUserIds,
        IReadOnlyList<Guid>? RecipientCustomerIds,
        IReadOnlyList<string>? RecipientEmails
    );

    [HttpPost("folders/{folderId:guid}/shares")]
    [Authorize(Policy = CloudStoragePermissions.ShareCreate)]
    [ProducesResponseType<CreatedShareLinkResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateForFolder(
        Guid folderId,
        CreateFolderShareLinkRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreatedShareLinkResponse>>(
            new CreateFolderShareLinkCommand(
                tenantId,
                actorId,
                scope,
                User.HasClaim("perm", CloudStoragePermissions.ShareManage),
                folderId,
                request.Visibility,
                request.Permission,
                request.Password,
                request.ExpiresAtUtc,
                request.MaxAccessCount,
                request.IsRecursive,
                request.AppliesToFutureItems,
                request.RecipientUserIds ?? [],
                request.RecipientCustomerIds ?? [],
                request.RecipientEmails ?? [],
                AuditContext()
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(ListForFolder), new { folderId }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("folders/{folderId:guid}/shares")]
    [Authorize(Policy = CloudStoragePermissions.FileView)]
    [ProducesResponseType<IReadOnlyList<ShareLinkResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForFolder(Guid folderId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out _, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<ShareLinkResponse>>>(
            new ListShareLinksForFolderQuery(tenantId, scope, folderId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("shares/shared-with-me")]
    [Authorize(Policy = CloudStoragePermissions.FileView)]
    [ProducesResponseType<IReadOnlyList<ShareLinkResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SharedWithMe(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    )
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<IReadOnlyList<ShareLinkResponse>>(
            new ListSharedWithMeQuery(tenantId, actorId, scope, skip, take),
            ct
        );
        return Ok(result);
    }

    [HttpDelete("shares/{shareLinkId:guid}")]
    [Authorize(Policy = CloudStoragePermissions.ShareRevoke)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid shareLinkId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new RevokeShareLinkCommand(tenantId, actorId, shareLinkId, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpdateExpirationRequest(DateTime NewExpiresAtUtc);

    [HttpPut("shares/{shareLinkId:guid}/expiration")]
    [Authorize(Policy = CloudStoragePermissions.ShareManage)]
    [ProducesResponseType<ShareLinkResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateExpiration(
        Guid shareLinkId,
        UpdateExpirationRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGet(out var tenantId, out _, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ShareLinkResponse>>(
            new UpdateShareExpirationCommand(tenantId, shareLinkId, request.NewExpiresAtUtc),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private RequestAuditContext AuditContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            correlation.CorrelationId
        );
}
