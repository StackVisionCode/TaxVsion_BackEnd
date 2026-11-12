namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Token que Auth emite mientras la persona debe configurar el MFA que su oficina exige. Lleva su propia audiencia
/// (ningún servicio la acepta salvo Auth, y solo en los endpoints de MFA) y una marca. Pieza aditiva: los servicios
/// que no la conocen lo rechazan solos por la audiencia.
/// </summary>
public static class MfaEnrollmentToken
{
    public const string Audience = "TaxVision.MfaEnrollment";
    public const string ClaimType = "mfa_enroll";
    public const string Pending = "pending";

    /// <summary>Código del 403 cuando el token solo-MFA intenta usar otra cosa.</summary>
    public const string ErrorCode = "Auth.MfaEnrollmentRequired";

    /// <summary>Lo único que se puede hacer con este token (además de los endpoints anónimos de login y refresh).</summary>
    public static readonly IReadOnlyList<(string Method, string Path)> AllowedRoutes =
    [
        ("POST", "/auth/mfa/totp/setup"),
        ("POST", "/auth/mfa/totp/confirm"),
        ("GET", "/auth/mfa/status"),
        ("POST", "/auth/logout"),
    ];

    public static bool IsPending(System.Security.Claims.ClaimsPrincipal user) =>
        user.HasClaim(claim => claim.Type == ClaimType && claim.Value == Pending);

    public static bool IsAllowed(string method, string path) =>
        AllowedRoutes.Any(route =>
            string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(route.Path, path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
        );
}
