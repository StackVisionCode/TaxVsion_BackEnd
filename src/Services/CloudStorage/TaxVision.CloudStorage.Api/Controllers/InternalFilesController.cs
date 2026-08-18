using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Files.Queries;
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
public sealed class InternalFilesController(IMessageBus bus) : ControllerBase
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
}
