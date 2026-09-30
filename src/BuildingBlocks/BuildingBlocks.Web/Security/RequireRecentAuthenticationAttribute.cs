using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BuildingBlocks.Web.Security;

/// <summary>
/// Step-up: la acción exige una reautenticación reciente (claim <c>reauth_at</c>, que solo trae el access
/// token emitido por <c>POST /auth/reauthenticate</c>). Si falta o venció responde 401
/// <c>Auth.ReauthenticationRequired</c> con <c>WWW-Authenticate: Bearer error="insufficient_user_authentication"</c>
/// y <c>max_age</c> (RFC 9470), para que el cliente pida la contraseña y reintente.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireRecentAuthenticationAttribute : Attribute, IAuthorizationFilter
{
    public const string ErrorCode = "Auth.ReauthenticationRequired";

    /// <summary>Antigüedad máxima de la reautenticación, en minutos.</summary>
    public int Minutes { get; init; } = 10;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true)
            return;

        var maxAge = TimeSpan.FromMinutes(Minutes);
        var now = DateTimeOffset.UtcNow;
        if (principal.TryGetReauthenticatedAt(out var reauthenticatedAt) && now - reauthenticatedAt <= maxAge)
            return;

        context.HttpContext.Response.Headers.WWWAuthenticate =
            $"Bearer error=\"insufficient_user_authentication\", error_description=\"A recent sign-in is required\", max_age={(int)maxAge.TotalSeconds}";
        context.Result = new ObjectResult(new Error(ErrorCode, "Please confirm your password to continue."))
        {
            StatusCode = StatusCodes.Status401Unauthorized,
        };
    }
}
