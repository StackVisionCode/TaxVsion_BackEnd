using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Marcas del token restringido, regla única de toda emisión (sesión, renovación y step-up). En el espacio de
/// trabajo y en la app: cambio obligatorio primero (D-12); si no, enrolamiento del MFA exigido. Nunca las dos.
/// </summary>
public static class RestrictedTokenMarks
{
    public static async Task<(bool PasswordChangeOnly, bool MfaEnrollmentOnly)> ResolveAsync(
        User user,
        SessionSurface surface,
        IMfaRepository mfa,
        bool mfaEnforced,
        CancellationToken ct
    )
    {
        var passwordChangeOnly = PasswordChangeRequirement.Applies(user, surface);
        var mfaEnrollmentOnly =
            !passwordChangeOnly
            && surface is SessionSurface.Workspace or SessionSurface.Mobile
            && (await MfaRequirement.DisposeAsync(user, mfa, ct, mfaEnforced)).MustEnroll;
        return (passwordChangeOnly, mfaEnrollmentOnly);
    }
}
