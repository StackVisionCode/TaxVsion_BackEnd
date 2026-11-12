using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;

namespace TaxVision.Auth.Api.Middleware;

/// <summary>
/// Un token solo-MFA (la oficina exige MFA y la persona no lo configuró) sirve únicamente para configurarlo: los
/// endpoints de <see cref="MfaEnrollmentToken.AllowedRoutes"/> y los anónimos (login, refresh). Cualquier otro
/// responde 403 <c>Auth.MfaEnrollmentRequired</c>. Corre justo después de la autenticación: denegar por defecto.
/// </summary>
public sealed class MfaEnrollmentGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (
            !MfaEnrollmentToken.IsPending(context.User)
            || MfaEnrollmentToken.IsAllowed(context.Request.Method, context.Request.Path)
            || context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null
        )
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new { code = MfaEnrollmentToken.ErrorCode, message = "Set up two-step verification to continue." },
            context.RequestAborted
        );
    }
}
