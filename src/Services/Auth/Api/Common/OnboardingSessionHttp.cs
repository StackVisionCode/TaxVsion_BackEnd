namespace TaxVision.Auth.Api.Common;

public static class OnboardingSessionHttp
{
    public const string CookieName = "__Host-taxvision-onboarding";

    public static string? ReadToken(HttpRequest request) =>
        request.Cookies.TryGetValue(CookieName, out var cookie) ? cookie : null;

    public static void AppendCookie(HttpResponse response, string token, DateTime expiresAtUtc)
    {
        response.Cookies.Append(
            CookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = new DateTimeOffset(expiresAtUtc),
                IsEssential = true,
            }
        );
    }
}
