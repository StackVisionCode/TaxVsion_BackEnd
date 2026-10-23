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
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Superficie de servicio a servicio, fuera de la ruta pública del Gateway. Sólo tokens de servicio:
/// ningún actor humano tiene nada que hacer acá.
/// </summary>
[ApiController]
[Route("internal/files")]
[Authorize]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalFilesController(IMessageBus bus, ICorrelationContext correlation) : ControllerBase
{
    /// <summary>
    /// En qué acabó el escaneo. Existe porque el evento del veredicto se publica una vez: un servicio
    /// que registró su referencia al archivo después de esa publicación se quedaría esperando para
    /// siempre. Devuelve el estado y nada más —ni ruta, ni nombre, ni tamaño—.
    /// </summary>
    [HttpGet("{fileId:guid}/scan-status")]
    [HasPermission(CloudStoragePermissions.FileView)]
    [RateLimit("cloudstorage.f.file_get")]
    [ProducesResponseType<FileScanStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetScanStatus(Guid fileId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out _, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<FileScanStatusResponse>>(
            new GetFileScanStatusQuery(tenantId, fileId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("reassign-form-participant")]
    [HasPermission(CloudStoragePermissions.ReassignFormParticipant)]
    [RateLimit("cloudstorage.g.file_manage")]
    [ProducesResponseType<ReassignFormParticipantFilesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReassignFormParticipant(
        ReassignFormParticipantFilesRequest request,
        CancellationToken ct
    )
    {
        if (!User.TryGet(out var tokenTenantId, out var actorId, out _))
            return Unauthorized();

        if (tokenTenantId != request.TenantId)
            return NotFound(FileErrors.NotFound);

        var result = await bus.InvokeAsync<Result<ReassignFormParticipantFilesResponse>>(
            new ReassignFormParticipantFilesCommand(actorId, request, AuditContext()),
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
