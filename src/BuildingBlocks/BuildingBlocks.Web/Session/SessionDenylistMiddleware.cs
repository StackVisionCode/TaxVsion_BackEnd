using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web.Session;

/// <summary>
/// RBAC Fase 6 (RBAC_Hardening_Plan.md) — rechaza access tokens cuya sesión (claim <c>sid</c>) fue
/// revocada y denylistada en Redis. Cubre la ventana entre la revocación y la expiración del JWT
/// (hasta 15 min). Antes de esta fase solo 3 de 14 servicios .NET (Auth, PaymentApp, PaymentClient)
/// tenían este chequeo — los otros 11 aceptaban un access token revocado hasta su <c>exp</c>.
/// Tokens M2M (sin claim <c>sid</c>) pasan sin chequear — la denylist es un concepto de sesión de
/// usuario, no de actor de servicio.
/// </summary>
public sealed class SessionDenylistMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ISessionDenylistReader denylist,
        IOptions<SessionDenylistOptions> options
    )
    {
        if (
            options.Value.Enabled
            && context.User.Identity is { IsAuthenticated: true }
            && context.User.TryGetSessionId(out var sessionId)
            && await denylist.IsSessionDeniedAsync(sessionId, context.RequestAborted)
        )
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new { type = "Auth.SessionRevoked", title = "Session has been revoked." }
            );
            return;
        }

        await next(context);
    }
}
