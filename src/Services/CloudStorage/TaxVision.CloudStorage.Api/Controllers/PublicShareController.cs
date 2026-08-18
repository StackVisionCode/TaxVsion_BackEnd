using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.CloudStorage.Application.Sharing;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Fase C3 — endpoint PUBLICO de resolucion de token. Sin [Authorize], rate
/// limited por IP+ruta. Respuestas uniformes: nunca distingue "no existe" de
/// "revocado/expirado/agotado" (anti-enumeracion, ver ResolvePublicShareHandler).
/// </summary>
[ApiController]
[Route("storage")]
[AllowAnonymous]
[EnableRateLimiting("share-public")]
public sealed class PublicShareController(IMessageBus bus) : ControllerBase
{
    /// <summary>Fase C4 — fileId es obligatorio cuando el token es de un link de tipo Folder (ver FolderShareCoverage).</summary>
    [HttpGet("public/{token}")]
    [RateLimitExempt(
        "Endpoint anonimo (sin JWT) — TieredRateLimitEvaluator solo soporta particion por Tenant/User, "
            + "asi que [RateLimit] fallaria abierto aqui. La proteccion real la da el limiter nativo "
            + "[EnableRateLimiting(\"share-public\")] (IP+ruta, 20/min FixedWindow), que se deja intacto."
    )]
    public async Task<IActionResult> ResolvePublic(
        string token,
        [FromQuery] string? password,
        [FromQuery] string? email,
        [FromQuery] Guid? fileId,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<ShareAccessResult>(
            new ResolvePublicShareQuery(token, password, email, fileId, RemoteIp(), UserAgent()),
            ct
        );
        return result.Outcome switch
        {
            ShareAccessOutcome.Redirect => Redirect(result.PresignedUrl!),
            ShareAccessOutcome.PasswordRequired => StatusCode(
                StatusCodes.Status401Unauthorized,
                new { requiresPassword = true }
            ),
            _ => NotFound(),
        };
    }

    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent() => Request.Headers.UserAgent.ToString();
}
