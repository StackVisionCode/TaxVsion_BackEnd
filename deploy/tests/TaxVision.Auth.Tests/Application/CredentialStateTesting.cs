using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Pone a un usuario en un estado de credencial puesto por la oficina.</summary>
internal static class CredentialStateTesting
{
    public static User Secure(this User user)
    {
        user.MarkResetRequired(
            CredentialAdministrationReason.Create(CredentialAdministrationReasonCode.SuspectedCompromise, null).Value,
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        return user;
    }

    public static User RequireChange(this User user)
    {
        user.RequirePasswordChange(
            CredentialAdministrationReason.Create(CredentialAdministrationReasonCode.OfficePolicy, null).Value,
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        return user;
    }
}
