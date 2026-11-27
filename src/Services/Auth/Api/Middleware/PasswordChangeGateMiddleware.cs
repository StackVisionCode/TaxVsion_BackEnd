using BuildingBlocks.ActorTypeAuthorization;
using Microsoft.AspNetCore.Authorization;

namespace TaxVision.Auth.Api.Middleware;

/// <summary>
/// El token de cambio obligatorio (03 §9.1) sirve únicamente para cambiar la contraseña y cerrar sesión, más los
/// endpoints anónimos (login, refresh). Cualquier otro responde 403 <c>Auth.PasswordChangeRequired</c>. Corre justo
/// después del gate del MFA: denegar por defecto.
/// </summary>
public sealed class PasswordChangeGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (
            !PasswordChangeToken.IsRequired(context.User)
            || PasswordChangeToken.IsAllowed(context.Request.Method, context.Request.Path)
            || context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null
        )
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(
            new { code = PasswordChangeToken.ErrorCode, message = "Update your password to continue." },
            context.RequestAborted
        );
    }
}
