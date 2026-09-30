using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace TaxVision.Auth.Api.Common;

/// <summary>Orígenes del Landing que pueden usar la sesión del Account (misma lista que su CORS).</summary>
public sealed class AccountSessionOptions
{
    public const string SectionName = "AccountSession";

    public string[] AllowedOrigins { get; set; } = [];

    public bool IsAllowed(string? origin) =>
        !string.IsNullOrWhiteSpace(origin)
        && AllowedOrigins.Any(allowed =>
            string.Equals(allowed.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
        );
}

/// <summary>
/// Refresh del Account en cookie HttpOnly: el Landing no lo ve nunca (access en memoria). SameSite=Strict y
/// el prefijo <c>__Host-</c> (Secure, Path=/, sin Domain) la atan a este host.
/// </summary>
public static class AccountSessionCookie
{
    public const string Name = "__Host-tv-account-rt";

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static void Append(HttpResponse response, string refreshToken, DateTime expiresAtUtc) =>
        response.Cookies.Append(Name, refreshToken, Options(new DateTimeOffset(expiresAtUtc, TimeSpan.Zero)));

    public static void Delete(HttpResponse response) =>
        response.Cookies.Delete(Name, Options(DateTimeOffset.UnixEpoch));

    private static CookieOptions Options(DateTimeOffset expires) =>
        new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expires,
            IsEssential = true,
        };
}

/// <summary>
/// CSRF de los endpoints que leen o escriben la cookie del Account: el <c>Origin</c> tiene que ser uno de los
/// orígenes permitidos del Landing. Sin <c>Origin</c> (o con otro) responde 403 <c>Auth.OriginNotAllowed</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAccountOriginAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<AccountSessionOptions>>().Value;
        if (options.IsAllowed(context.HttpContext.Request.Headers.Origin.ToString()))
            return;

        context.Result = new ObjectResult(new Error("Auth.OriginNotAllowed", "This request isn't allowed from here."))
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
