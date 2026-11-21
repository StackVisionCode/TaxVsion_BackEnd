using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Files;
using Wolverine;

namespace TaxVision.Correspondence.Api.Controllers;

/// <summary>
/// Visibilidad por archivo para File Library (la llama CloudStorage con el token del usuario): adjuntos de correo que
/// quien mira vería en Mail (Sent o una conversación). Solo staff; TenantId siempre del JWT.
/// </summary>
[ApiController]
[Route("correspondence/files")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin)]
public sealed class CorrespondenceFilesController(
    IMessageBus bus,
    IMailboxVisibilityResolver visibility,
    ICustomerAccessResolver customerAccess,
    IUserPermissionsSource permissions
) : ControllerBase
{
    private static readonly Error InvalidRequest = new(
        "Correspondence.Files.InvalidVisibilityRequest",
        $"fileIds is required and must have at most {FileVisibilityLimits.Batch} entries; purpose must be "
            + $"'{FileVisibilityPurposes.List}' or '{FileVisibilityPurposes.Download}'."
    );

    // ---------- POST /correspondence/files/visibility ----------
    [HttpPost("visibility")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.f.file_visibility")]
    [ProducesResponseType<FileVisibilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Visibility([FromBody] FileVisibilityRequest? request, CancellationToken ct)
    {
        if (!IsValid(request))
            return BadRequest(InvalidRequest);
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var restrictTo = await customerAccess.ResolveRestrictedUserIdAsync(User, ct);
        // Un entrante se baja con su propio permiso (como en Mail); sin él la fila sale sin descarga.
        var canDownloadIncoming = await permissions.HasPermissionAsync(
            User,
            CorrespondencePermissions.AttachmentDownload,
            ct
        );
        var viewer = User.TryGetUserId(out var userId) ? userId : Guid.Empty;
        var result = await bus.InvokeAsync<FileVisibilityResponse>(
            new GetVisibleMailFilesQuery(
                tenantId,
                request!.FileIds,
                visible,
                restrictTo,
                viewer,
                canDownloadIncoming,
                request.Purpose
            ),
            ct
        );
        return Ok(result);
    }

    private static bool IsValid(FileVisibilityRequest? request) =>
        request?.FileIds is { Count: > 0 and <= FileVisibilityLimits.Batch }
        && request.Purpose is null or FileVisibilityPurposes.List or FileVisibilityPurposes.Download;
}
