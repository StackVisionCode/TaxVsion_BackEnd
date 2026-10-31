using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Topes de tamaño de subida por carpeta/plan/oficina (D-33). Solo PlatformAdmin: ningún tenant
/// edita sus propios topes. La whitelist de tipos sigue en código; acá solo se administra el tamaño.
/// </summary>
[ApiController]
[Route("storage/admin/upload-policies")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(CloudStoragePermissions.UploadPolicyManage)]
public sealed class UploadSizePolicyAdminController(IMessageBus bus) : ControllerBase
{
    public sealed record SetUploadSizePolicyRequest(long MaxFileSizeBytes);

    [HttpGet]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<UploadSizePoliciesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await bus.InvokeAsync<UploadSizePoliciesResponse>(new ListUploadSizePoliciesQuery(), ct));

    [HttpGet("audit")]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<IReadOnlyList<UploadSizePolicyAuditView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Audit(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    ) =>
        Ok(
            await bus.InvokeAsync<IReadOnlyList<UploadSizePolicyAuditView>>(
                new ListUploadSizePolicyAuditQuery(skip, take),
                ct
            )
        );

    [HttpPut("global/{folderType}")]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<SetUploadSizePolicyResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> SetGlobal(string folderType, SetUploadSizePolicyRequest request, CancellationToken ct) =>
        SetAsync(UploadSizePolicyScope.Global, folderType, null, null, request, ct);

    [HttpPut("plans/{planCode}/{folderType}")]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<SetUploadSizePolicyResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> SetPlan(
        string planCode,
        string folderType,
        SetUploadSizePolicyRequest request,
        CancellationToken ct
    ) => SetAsync(UploadSizePolicyScope.Plan, folderType, planCode, null, request, ct);

    [HttpPut("tenants/{tenantId:guid}/{folderType}")]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<SetUploadSizePolicyResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> SetTenant(
        Guid tenantId,
        string folderType,
        SetUploadSizePolicyRequest request,
        CancellationToken ct
    ) => SetAsync(UploadSizePolicyScope.Tenant, folderType, null, tenantId, request, ct);

    [HttpDelete("plans/{planCode}/{folderType}")]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<RemoveUploadSizePolicyResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> RemovePlan(string planCode, string folderType, CancellationToken ct) =>
        RemoveAsync(UploadSizePolicyScope.Plan, folderType, planCode, null, ct);

    [HttpDelete("tenants/{tenantId:guid}/{folderType}")]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<RemoveUploadSizePolicyResponse>(StatusCodes.Status200OK)]
    public Task<IActionResult> RemoveTenant(Guid tenantId, string folderType, CancellationToken ct) =>
        RemoveAsync(UploadSizePolicyScope.Tenant, folderType, null, tenantId, ct);

    private async Task<IActionResult> SetAsync(
        UploadSizePolicyScope scope,
        string folderType,
        string? planCode,
        Guid? tenantId,
        SetUploadSizePolicyRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();
        if (!TryParseFolderType(folderType, out var parsed))
            return BadRequest(UploadSizePolicyErrors.InvalidFolderType);

        var result = await bus.InvokeAsync<Result<SetUploadSizePolicyResponse>>(
            new SetUploadSizePolicyCommand(scope, parsed, planCode, tenantId, request.MaxFileSizeBytes, actorId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private async Task<IActionResult> RemoveAsync(
        UploadSizePolicyScope scope,
        string folderType,
        string? planCode,
        Guid? tenantId,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();
        if (!TryParseFolderType(folderType, out var parsed))
            return BadRequest(UploadSizePolicyErrors.InvalidFolderType);

        var result = await bus.InvokeAsync<Result<RemoveUploadSizePolicyResponse>>(
            new RemoveUploadSizePolicyCommand(scope, parsed, planCode, tenantId, actorId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // Solo nombres del enum ("Recordings"), nunca su valor numérico: "3" también parsea y no es un tipo válido.
    private static bool TryParseFolderType(string raw, out FolderType folderType) =>
        Enum.TryParse(raw, ignoreCase: true, out folderType)
        && !long.TryParse(raw, out _)
        && Enum.IsDefined(folderType);
}
