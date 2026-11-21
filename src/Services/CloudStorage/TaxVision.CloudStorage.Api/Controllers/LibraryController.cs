using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Library;
using TaxVision.CloudStorage.Application.Library.Queries;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Biblioteca de archivos (solo consulta). Solo el personal de la oficina: ni portal, ni servicios, ni
/// PlatformAdmin. Cada origen exige además su propio permiso y módulo (<see cref="ILibraryAccessResolver"/>).
/// </summary>
[ApiController]
[Route("storage/library")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin)]
// AllowActorTypes siempre deja pasar a PlatformAdmin.
[ExcludePlatformAdmin]
public sealed class LibraryController(
    IMessageBus bus,
    ICorrelationContext correlation,
    ILibraryAccessResolver accessResolver,
    IStorageActorScopeResolver scopeResolver
) : ControllerBase
{
    [HttpGet("sources")]
    [HasPermission(CloudStoragePermissions.LibraryView)]
    [RateLimit("cloudstorage.f.library_sources")]
    [ProducesResponseType<LibrarySourcesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Sources(CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();

        var access = await accessResolver.ResolveAsync(User, tenantId, actorId, ct);
        return Ok(await bus.InvokeAsync<LibrarySourcesResponse>(new GetLibrarySourcesQuery(access), ct));
    }

    [HttpGet]
    [HasPermission(CloudStoragePermissions.LibraryView)]
    [RateLimit("cloudstorage.h.library_search")]
    [ProducesResponseType<LibrarySearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> Search([FromQuery] LibrarySearchRequest request, CancellationToken ct) =>
        SearchAsync(request, forcedSource: null, ct);

    /// <summary>Búsqueda dentro de Documents: pide el permiso de Documents, no el de la biblioteca.</summary>
    [HttpGet("documents")]
    [HasPermission(CloudStoragePermissions.FileView)]
    [RateLimit("cloudstorage.h.library_search")]
    [ProducesResponseType<LibrarySearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> SearchDocuments([FromQuery] LibrarySearchRequest request, CancellationToken ct) =>
        SearchAsync(request, LibrarySource.Documents, ct);

    /// <summary>URL de vista previa y descarga (siempre adjunto) de un archivo que la búsqueda devolvería.</summary>
    [HttpPost("files/{fileId:guid}/download-url")]
    [HasPermission(CloudStoragePermissions.LibraryView)]
    [HasPermission(CloudStoragePermissions.FileDownload)]
    [RateLimit("cloudstorage.f.library_download")]
    [ProducesResponseType<DownloadUrlResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> IssueDownloadUrl(Guid fileId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var access = await accessResolver.ResolveAsync(User, tenantId, actorId, ct);
        scope = await scopeResolver.ResolveAsync(User, tenantId, actorId, scope, ct);
        var result = await bus.InvokeAsync<Result<DownloadUrlResponse>>(
            new IssueLibraryDownloadUrlQuery(tenantId, actorId, access, scope, fileId, AuditContext()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(StatusOf(result.Error), result.Error);
    }

    private async Task<IActionResult> SearchAsync(
        LibrarySearchRequest request,
        LibrarySource? forcedSource,
        CancellationToken ct
    )
    {
        var filter = LibraryFilter.Create(request, forcedSource);
        if (filter.IsFailure)
            return BadRequest(filter.Error);
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();

        var access = await accessResolver.ResolveAsync(User, tenantId, actorId, ct);
        return Ok(
            await bus.InvokeAsync<LibrarySearchResponse>(new SearchLibraryQuery(tenantId, access, filter.Value), ct)
        );
    }

    // El dueño de un origen sin respuesta es un 503 (el mapeo general lo dejaría en 400).
    private static int StatusOf(Error error) =>
        error == LibraryErrors.SourceUnavailable ? StatusCodes.Status503ServiceUnavailable : error.ToHttpStatusCode();

    private RequestAuditContext AuditContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            correlation.CorrelationId
        );
}
