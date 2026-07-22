using System.IO.Compression;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Files.LegalHold;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Application.Folders;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

[ApiController]
[Route("storage/files")]
[Authorize]
public sealed class FilesController(
    IMessageBus bus,
    ICorrelationContext correlation,
    IObjectStorage storage,
    IOptions<CloudStorageOptions> storageOptions
) : ControllerBase
{
    [HttpPost("uploads")]
    [HasPermission(CloudStoragePermissions.FileUpload)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [ProducesResponseType<InitiatedUploadResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> InitiateUpload(InitiateUploadRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<InitiatedUploadResponse>>(
            new InitiateUploadCommand(tenantId, actorId, scope, request, AuditContext()),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { fileId = result.Value.FileId }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{fileId:guid}/complete")]
    [HasPermission(CloudStoragePermissions.FileUpload)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CompleteUpload(Guid fileId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new CompleteUploadCommand(tenantId, actorId, scope, fileId, AuditContext()),
            ct
        );
        return result.IsSuccess
            ? AcceptedAtAction(nameof(GetById), new { fileId }, new { fileId, status = "PendingScan" })
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Fase U — arranca un upload multiparte: el browser sube cada parte directo a MinIO con las URLs devueltas, sin pasar por CloudStorage.</summary>
    [HttpPost("uploads/initiate-multipart")]
    [HasPermission(CloudStoragePermissions.FileUpload)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [ProducesResponseType<InitiatedMultipartUploadResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> InitiateMultipartUpload(
        InitiateMultipartUploadRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<InitiatedMultipartUploadResponse>>(
            new InitiateMultipartUploadCommand(tenantId, actorId, scope, request, AuditContext()),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { fileId = result.Value.FileId }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Fase U — ensambla las partes ya subidas y sigue el mismo pipeline que el complete de un solo POST (verificar tamano, disparar escaneo).</summary>
    [HttpPost("{fileId:guid}/complete-multipart")]
    [HasPermission(CloudStoragePermissions.FileUpload)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> CompleteMultipartUpload(
        Guid fileId,
        CompleteMultipartUploadRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new CompleteMultipartUploadCommand(tenantId, actorId, scope, fileId, request, AuditContext()),
            ct
        );
        return result.IsSuccess
            ? AcceptedAtAction(nameof(GetById), new { fileId }, new { fileId, status = "PendingScan" })
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{fileId:guid}")]
    [HasPermission(CloudStoragePermissions.FileView)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [ProducesResponseType<FileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid fileId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out _, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<FileResponse>>(new GetFileQuery(tenantId, scope, fileId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [HasPermission(CloudStoragePermissions.FileView)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [ProducesResponseType<IReadOnlyList<FileResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    )
    {
        if (!User.TryGet(out var tenantId, out _, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<IReadOnlyList<FileResponse>>(
            new ListFilesQuery(tenantId, scope, skip, take),
            ct
        );
        return Ok(result);
    }

    [HttpPost("{fileId:guid}/download-url")]
    [HasPermission(CloudStoragePermissions.FileDownload)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [ProducesResponseType<DownloadUrlResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> IssueDownloadUrl(Guid fileId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<DownloadUrlResponse>>(
            new IssueDownloadUrlQuery(tenantId, actorId, scope, fileId, AuditContext()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Fase B2/B2.1 — descarga varios archivos y/o carpetas completas (recursivo)
    /// como un unico .zip, streameado directo al Response.Body. Deliberadamente
    /// NO pasa por bus.InvokeAsync para los bytes en si (un Wolverine handler no
    /// tiene acceso a HttpResponse) — solo la validacion/auditoria/resolucion de
    /// carpetas (PrepareZipDownloadQuery) va por el bus, igual que el resto del
    /// controller. Si MinIO falla a mitad de stream, los headers ya se mandaron
    /// (200 + Content-Type) y la conexion se corta en seco — no hay forma de
    /// degradar a una respuesta de error prolija una vez que el streaming
    /// arranco; es una limitacion inherente a este tipo de endpoint, no un gap.
    /// </summary>
    [HttpPost("zip")]
    [HasPermission(CloudStoragePermissions.FileDownload)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [EnableRateLimiting("zip-download")]
    public async Task<IActionResult> DownloadZip(ZipDownloadRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var plan = await bus.InvokeAsync<Result<ZipDownloadPlan>>(
            new PrepareZipDownloadQuery(
                tenantId,
                actorId,
                scope,
                request.FileIds,
                request.FolderIds ?? [],
                AuditContext()
            ),
            ct
        );
        if (plan.IsFailure)
            return StatusCode(plan.Error.ToHttpStatusCode(), plan.Error);

        var archiveName = $"taxvision-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{archiveName}\"";

        var mainBucket = storageOptions.Value.MainBucket;
        using (var zip = new ZipArchive(Response.Body, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in plan.Value.Entries)
            {
                var zipEntry = zip.CreateEntry(entry.EntryName, CompressionLevel.Fastest);
                await using var entryStream = zipEntry.Open();
                await storage.DownloadAsync(mainBucket, entry.ObjectKey, entryStream, ct);
            }
        }
        return new EmptyResult();
    }

    [HttpDelete("{fileId:guid}")]
    [HasPermission(CloudStoragePermissions.FileDelete)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new DeleteFileCommand(tenantId, actorId, scope, fileId, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record MoveToFolderRequest(Guid? FolderId);

    /// <summary>Fase C2 — mueve el archivo a otra carpeta navegable (o a la raiz con FolderId=null). No toca MinIO.</summary>
    [HttpPut("{fileId:guid}/folder")]
    [HasPermission(CloudStoragePermissions.FolderManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MoveToFolder(Guid fileId, MoveToFolderRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new MoveFileToFolderCommand(tenantId, actorId, scope, fileId, request.FolderId, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record LegalHoldRequest(string Reason);

    /// <summary>Fase L1.2 — bloquea purge/hard-delete/soft-delete. Platform-only (cloudstorage.legal.manage).</summary>
    [HttpPut("{fileId:guid}/legal-hold")]
    [HasPermission(CloudStoragePermissions.LegalManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetLegalHold(Guid fileId, LegalHoldRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetLegalHoldCommand(tenantId, actorId, fileId, request.Reason, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{fileId:guid}/legal-hold")]
    [HasPermission(CloudStoragePermissions.LegalManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LiftLegalHold(Guid fileId, LegalHoldRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new LiftLegalHoldCommand(tenantId, actorId, fileId, request.Reason, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private RequestAuditContext AuditContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            correlation.CorrelationId
        );
}
