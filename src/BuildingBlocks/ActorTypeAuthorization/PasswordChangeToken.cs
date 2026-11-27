namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Token que Auth emite cuando la oficina pidió un cambio de contraseña obligatorio (03 §9.1). Audiencia propia
/// (solo Auth la acepta) y una marca. Pieza aditiva, calcada de <see cref="MfaEnrollmentToken"/>: los servicios que
/// no la conocen lo rechazan solos por la audiencia.
/// </summary>
public static class PasswordChangeToken
{
    public const string Audience = "TaxVision.PasswordChange";
    public const string ClaimType = "pwd_change";
    public const string Required = "required";

    /// <summary>Claim aditivo: "true"/"false" según la cuenta tenga MFA; el front pide 12 o 15 caracteres (D-13).</summary>
    public const string MfaClaimType = "pwd_mfa";

    /// <summary>Código del 403 cuando el token de cambio intenta usar otra cosa.</summary>
    public const string ErrorCode = "Auth.PasswordChangeRequired";

    /// <summary>Lo único que se puede hacer con este token (además de los endpoints anónimos de login y refresh).</summary>
    public static readonly IReadOnlyList<(string Method, string Path)> AllowedRoutes =
    [
        ("POST", "/auth/password/change"),
        ("POST", "/auth/logout"),
    ];

    public static bool IsRequired(System.Security.Claims.ClaimsPrincipal user) =>
        user.HasClaim(claim => claim.Type == ClaimType && claim.Value == Required);

    public static bool IsAllowed(string method, string path) =>
        AllowedRoutes.Any(route =>
            string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(route.Path, path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
        );
}
