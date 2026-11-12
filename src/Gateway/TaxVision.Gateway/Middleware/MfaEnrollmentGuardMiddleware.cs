using BuildingBlocks.ActorTypeAuthorization;

namespace TaxVision.Gateway.Middleware;

/// <summary>
/// Segunda capa del token solo-MFA: el gateway no lo deja salir de <c>/auth/</c>. Auth decide qué endpoint de MFA
/// acepta y los demás servicios lo rechazan por la audiencia; esto corta antes de llegar a ellos.
/// </summary>
public sealed class MfaEnrollmentGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (MfaEnrollmentToken.IsPending(context.User) && !context.Request.Path.StartsWithSegments("/auth"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { code = MfaEnrollmentToken.ErrorCode, message = "Set up two-step verification to continue." },
                context.RequestAborted
            );
            return;
        }

        await next(context);
    }
}
