using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// Convierte el 403 del middleware de autorización en un cuerpo RFC 9457 con <c>code</c> y
/// <c>reason</c> (A5 del plan). Antes, una policy <c>[HasPermission]</c> fallida producía un 403
/// **con el cuerpo vacío**: el frontend no podía distinguir "no tenés el permiso" de "tu plan no
/// incluye el módulo" ni de un 403 de otra capa, así que mostraba siempre la misma pantalla.
///
/// <para>
/// Envuelve al handler default en vez de reemplazarlo: el challenge (401) y todo lo que no sea un
/// forbid siguen exactamente como antes. Un 403 que ya escribió alguien más (un filtro de MVC, que
/// corre antes) tampoco se toca.
/// </para>
///
/// <para>
/// La razón la deja <see cref="PermissionPolicyProvider"/> en <c>HttpContext.Items</c> al denegar
/// (ver <see cref="AuthorizationDenial.Record"/>). Si no hay ninguna anotada —una policy propia del
/// servicio, ajena a <c>[HasPermission]</c>— se responde la denegación genérica de permiso, que es la
/// lectura correcta para cualquier policy fallida.
/// </para>
/// </summary>
public sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult
    )
    {
        if (!authorizeResult.Forbidden || context.Response.HasStarted)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var denial = AuthorizationDenial.Recorded(context) ?? AuthorizationDenial.PermissionDenied;
        await denial.WriteAsync(context);
    }
}
