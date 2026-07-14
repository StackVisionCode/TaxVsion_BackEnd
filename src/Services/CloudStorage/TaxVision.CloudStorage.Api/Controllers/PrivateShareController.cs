using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Fase C3 — endpoint PRIVADO de resolucion de token: requiere [Authorize] y el
/// token no alcanza por si solo. Fail-closed: tenant del JWT debe coincidir con
/// el tenant del link (ver ResolvePrivateShareHandler) aunque el token sea valido.
/// </summary>
[ApiController]
[Route("cloud-storage")]
[Authorize]
public sealed class PrivateShareController(IMessageBus bus) : ControllerBase
{
    /// <summary>Fase C4 — fileId es obligatorio cuando el token es de un link de tipo Folder (ver FolderShareCoverage).</summary>
    [HttpGet("private/{token}")]
    public async Task<IActionResult> ResolvePrivate(string token, [FromQuery] Guid? fileId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<ShareAccessResult>(
            new ResolvePrivateShareQuery(token, tenantId, actorId, scope, fileId, RemoteIp(), UserAgent()),
            ct
        );
        return result.Outcome == ShareAccessOutcome.Redirect ? Redirect(result.PresignedUrl!) : NotFound();
    }

    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent() => Request.Headers.UserAgent.ToString();
}
