using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Application.Feeds.Commands;
using TaxVision.Calendar.Application.Feeds.Queries;
using TaxVision.Calendar.Application.Observability;
using Wolverine;

namespace TaxVision.Calendar.Api.Controllers;

/// <summary>
/// El feed `.ics` y su credencial.
///
/// <para>
/// La descarga no lleva sesión: el token de la URL <b>es</b> la credencial, porque Google y Outlook
/// pollean el archivo sin poder autenticarse. Emitirlo y revocarlo sí requieren sesión, y cada usuario
/// sólo puede sobre el suyo.
/// </para>
/// </summary>
[ApiController]
[Route("calendar/feed")]
public sealed class CalendarFeedController(
    IMessageBus bus,
    ICalendarFeedCache cache,
    ICalendarMetrics metrics,
    ILogger<CalendarFeedController> logger
) : ControllerBase
{
    /// <summary>Emite la URL y revoca la anterior. El valor crudo se ve acá y en ningún otro sitio.</summary>
    [HttpPost("token")]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.g.update")]
    public async Task<IActionResult> IssueToken(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IssuedFeedToken>>(new IssueFeedTokenCommand(tenantId, userId), ct);

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpDelete("token")]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [HasPermission(CalendarPermissions.Read)]
    [RateLimit("calendar.g.update")]
    public async Task<IActionResult> RevokeToken(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(new RevokeFeedTokenCommand(tenantId, userId), ct);

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    /// <summary>
    /// La descarga. El límite va por token y no por IP: Google pollea desde direcciones rotativas, así
    /// que limitar por IP no frena nada y castiga a quien comparte salida.
    /// </summary>
    [HttpGet("{userId:guid}/{token}.ics")]
    [AllowAnonymous]
    [RateLimit("calendar.h.ics")]
    [Produces("text/calendar")]
    public async Task<IActionResult> Download(Guid userId, string token, CancellationToken ct)
    {
        Result<string> result;
        try
        {
            result = await bus.InvokeAsync<Result<string>>(new GetCalendarFeedQuery(userId, token), ct);
        }
        catch (Exception ex)
        {
            // El respaldo va acá y no dentro del handler: la transacción de Wolverine se abre antes
            // del cuerpo del handler, así que un catch allá adentro nunca ve un fallo de conexión.
            // Medido con la base puesta offline y el servicio corriendo.
            //
            // Un 500 hace que Google deje de actualizar y el usuario se queda sin agenda; servirle la
            // de la última vez que funcionó es peor que la verdad y mucho mejor que nada.
            var stale = await cache.GetAsync(CacheKey.For(token), ct);
            if (stale is null)
                throw;

            logger.LogWarning(ex, "Calendar feed served from the last good copy: the live read failed.");
            metrics.RecordIcsFeedStale();

            Response.Headers.CacheControl = "private, max-age=900";
            return Content(stale, "text/calendar; charset=utf-8");
        }

        if (result.IsFailure)
            return NotFound();

        Response.Headers.CacheControl = "private, max-age=900";
        return Content(result.Value, "text/calendar; charset=utf-8");
    }
}
