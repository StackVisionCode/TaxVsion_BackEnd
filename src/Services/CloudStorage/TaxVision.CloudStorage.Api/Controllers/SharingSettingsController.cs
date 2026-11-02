using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Platform;
using TaxVision.CloudStorage.Application.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Reglas de compartir de la oficina: vida por defecto y máxima de los enlaces externos, contraseña
/// obligatoria y su largo mínimo, intentos antes de bloquear y tope de descargas.
/// </summary>
[ApiController]
[Route("storage/settings/sharing")]
[Authorize]
public sealed class SharingSettingsController(IMessageBus bus) : ControllerBase
{
    public sealed record UpdateSharingPolicyRequest(
        bool AllowLinkOnlyExternalShares,
        bool RequirePasswordOnLinkShares,
        int DefaultLifetimeDays,
        int MaxLifetimeDays,
        int MinPasswordLength,
        int MaxPasswordAttempts,
        int? MaxDownloads
    );

    // ---------- GET /storage/settings/sharing ----------
    [HttpGet]
    [HasPermission(CloudStoragePermissions.SettingsManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<SharingPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SharingPolicyResponse>>(new GetSharingPolicyQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- GET /storage/shares/policy ----------
    // Lo que necesita el diálogo de compartir (topes, contraseña, si hay enlaces públicos): lo lee quien
    // crea enlaces, sin el permiso de ajustes.
    [HttpGet("/storage/shares/policy")]
    [HasPermission(CloudStoragePermissions.ShareCreate)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("cloudstorage.f.file_get")]
    [ProducesResponseType<SharingPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForSharing(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SharingPolicyResponse>>(new GetSharingPolicyQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PUT /storage/settings/sharing ----------
    [HttpPut]
    [HasPermission(CloudStoragePermissions.SettingsManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<SharingPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(UpdateSharingPolicyRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<SharingPolicyResponse>>(
            new UpdateSharingPolicyCommand(
                tenantId,
                request.AllowLinkOnlyExternalShares,
                request.RequirePasswordOnLinkShares,
                request.DefaultLifetimeDays,
                request.MaxLifetimeDays,
                request.MinPasswordLength,
                request.MaxPasswordAttempts,
                request.MaxDownloads
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

/// <summary>
/// Valores operativos de CloudStorage (PlatformAdmin): vigencia de las URLs firmadas, reserva de subida,
/// papelera, topes de ZIP y bloqueo de enlaces por contraseña. Cambian sin redeploy.
/// </summary>
[ApiController]
[Route("storage/admin/platform-settings")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(CloudStoragePermissions.UploadPolicyManage)]
public sealed class StoragePlatformSettingsController(IMessageBus bus) : ControllerBase
{
    public sealed record UpdateStoragePlatformSettingsRequest(
        int SignedUrlMinutes,
        int ShareUrlMinutes,
        int UploadReservationHours,
        int RecycleBinRetentionDays,
        int MaxZipFiles,
        int MaxZipFolders,
        long MaxZipAggregateBytes,
        int SharePasswordLockMinutes
    );

    [HttpGet]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<StoragePlatformSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await bus.InvokeAsync<StoragePlatformSettingsResponse>(new GetStoragePlatformSettingsQuery(), ct));

    [HttpPut]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<StoragePlatformSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(UpdateStoragePlatformSettingsRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<StoragePlatformSettingsResponse>>(
            new UpdateStoragePlatformSettingsCommand(
                request.SignedUrlMinutes,
                request.ShareUrlMinutes,
                request.UploadReservationHours,
                request.RecycleBinRetentionDays,
                request.MaxZipFiles,
                request.MaxZipFolders,
                request.MaxZipAggregateBytes,
                request.SharePasswordLockMinutes,
                actorId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
