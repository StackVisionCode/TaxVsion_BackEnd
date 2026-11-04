using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Qué se puede subir y dónde: la política efectiva para los frontends (el <c>accept</c> del input y la
/// validación previa salen de acá, no de constantes del front) y la restricción de la oficina para lo que
/// suben sus clientes desde el portal.
/// </summary>
[ApiController]
[Route("storage")]
[Authorize]
public sealed class FilePolicyController(IMessageBus bus) : ControllerBase
{
    // ---------- GET /storage/upload-policy?folderType=Documents ----------
    [HttpGet("upload-policy")]
    [HasPermission(CloudStoragePermissions.FileUpload)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [RateLimit("cloudstorage.f.file_get")]
    [ProducesResponseType<UploadPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUploadPolicy([FromQuery] string folderType, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out _, out var scope))
            return Unauthorized();
        if (!Enum.TryParse<FolderType>(folderType, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return BadRequest(UploadSizePolicyErrors.InvalidFolderType);

        var result = await bus.InvokeAsync<Result<UploadPolicyResponse>>(
            new GetUploadPolicyQuery(tenantId, scope, parsed),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- GET /storage/settings/portal-uploads ----------
    [HttpGet("settings/portal-uploads")]
    [HasPermission(CloudStoragePermissions.SettingsManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<PortalUploadSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPortalUploads(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PortalUploadSettingsResponse>>(
            new GetPortalUploadSettingsQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record UpdatePortalUploadsRequest(IReadOnlyList<string>? FileTypeGroups, long? MaxFileBytes);

    // ---------- PUT /storage/settings/portal-uploads ----------
    // La oficina solo puede RESTRINGIR (grupos dentro de los del plan; null = igual que el plan).
    [HttpPut("settings/portal-uploads")]
    [HasPermission(CloudStoragePermissions.SettingsManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<PortalUploadSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePortalUploads(UpdatePortalUploadsRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PortalUploadSettingsResponse>>(
            new UpdatePortalUploadSettingsCommand(tenantId, request.FileTypeGroups, request.MaxFileBytes),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

/// <summary>
/// Catálogo de tipos de archivo y políticas por carpeta (PlatformAdmin). Agregar un tipo o cambiar lo que
/// admite una carpeta no requiere redeploy; la lista negra de extensiones peligrosas sigue en código.
/// </summary>
[ApiController]
[Route("storage/admin/file-policies")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
[HasPermission(CloudStoragePermissions.UploadPolicyManage)]
public sealed class FilePolicyAdminController(IMessageBus bus) : ControllerBase
{
    public sealed record UpsertFileTypeRequest(string Group, IReadOnlyList<string> ContentTypes, bool IsEnabled = true);

    public sealed record UpdateFolderPolicyRequest(
        long MaxSizeBytes,
        IReadOnlyList<string> AllowedExtensions,
        bool UsesPlanFileTypes,
        bool UsesPlanFileSize
    );

    [HttpGet]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<FilePolicyCatalogResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await bus.InvokeAsync<FilePolicyCatalogResponse>(new ListFilePolicyCatalogQuery(), ct));

    // ---------- PUT /storage/admin/file-policies/file-types/{extension} ----------
    [HttpPut("file-types/{extension}")]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<FileTypeView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertFileType(
        string extension,
        UpsertFileTypeRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<FileTypeView>>(
            new UpsertFileTypeCommand(extension, request.Group, request.ContentTypes, request.IsEnabled, actorId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- PUT /storage/admin/file-policies/folders/{folderType} ----------
    [HttpPut("folders/{folderType}")]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<FolderPolicyView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateFolderPolicy(
        string folderType,
        UpdateFolderPolicyRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGetUserId(out var actorId))
            return Unauthorized();
        if (!Enum.TryParse<FolderType>(folderType, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return BadRequest(UploadSizePolicyErrors.InvalidFolderType);

        var result = await bus.InvokeAsync<Result<FolderPolicyView>>(
            new UpdateFolderPolicyCommand(
                parsed,
                request.MaxSizeBytes,
                request.AllowedExtensions,
                request.UsesPlanFileTypes,
                request.UsesPlanFileSize,
                actorId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
