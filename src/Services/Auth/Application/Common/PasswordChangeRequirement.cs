using TaxVision.Auth.Domain.RefreshTokens;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Regla única del cambio obligatorio (03 §9.2): en el espacio de trabajo y en la app, una cuenta con
/// <c>ChangeRequired</c> solo recibe el token de cambio. La usan la emisión de tokens y el flag de las respuestas.
/// </summary>
public static class PasswordChangeRequirement
{
    public static bool Applies(User user, SessionSurface surface) =>
        surface is SessionSurface.Workspace or SessionSurface.Mobile
        && user.CredentialStatus == CredentialStatus.ChangeRequired;
}
