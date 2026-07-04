using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Api.Middleware;

/// <summary>
/// Rechaza access tokens cuya sesión (claim sid) fue revocada y denylistada en
/// Redis. Cubre la ventana entre la revocación y la expiración del JWT (15 min).
/// </summary>
public sealed class SessionDenylistMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAccessTokenDenylist denylist)
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
