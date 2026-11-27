using BuildingBlocks.Results;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Quién puede tener una sesión del Account del Landing: solo el TenantAdmin (el PlatformAdmin opera por
/// sus herramientas de soporte, no por el Account de un tenant). El Account no enrola MFA: si la política lo
/// exige y el usuario no tiene método, primero entra al workspace.
/// </summary>
public static class AccountSurfacePolicy
{
    public static readonly Error AdminOnly = new(
        "Auth.AccountAdminOnly",
        "Only office administrators can manage the subscription."
    );

    public static readonly Error MfaSetupRequired = new(
        "Auth.AccountMfaSetupRequired",
        "Set up two-step verification in your office workspace first, then come back."
    );

    /// <summary>Con el cambio obligatorio pendiente, primero se cambia la contraseña en el workspace (03 §9.2 paso 5).</summary>
    public static readonly Error PasswordChangeRequired = new(
        "Auth.PasswordChangeRequired",
        "Update your password in your office workspace first, then come back."
    );

    public static Error? Check(User user, bool mustEnrollMfa)
    {
        if (user.ActorType != UserActorType.TenantAdmin)
            return AdminOnly;

        if (user.CredentialStatus == CredentialStatus.ChangeRequired)
            return PasswordChangeRequired;

        return mustEnrollMfa ? MfaSetupRequired : null;
    }
}
