using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Api.Common;

/// <summary>
/// Rechaza access tokens cuya sesión (claim sid) fue revocada y denylistada en Redis por
/// Auth. Cubre la ventana entre la revocación y la expiración del JWT (15 min) — mismo
/// mecanismo que Auth usa consigo mismo, aquí en modo solo-lectura.
/// </summary>
public sealed class SessionDenylistMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ISessionDenylistReader denylist)
    {
        if (
            context.User.Identity is { IsAuthenticated: true }
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
