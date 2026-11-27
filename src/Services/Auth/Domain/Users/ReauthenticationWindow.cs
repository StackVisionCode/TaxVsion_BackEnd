using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Users;

/// <summary>
/// Step-up en llamadas M2M (03 §6.5, AJ-11): Customer reenvía el <c>reauth_at</c> del usuario y Auth exige que no
/// tenga más de 10 minutos ni esté en el futuro.
/// </summary>
public static class ReauthenticationWindow
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    public static Result Validate(DateTime reauthenticatedAtUtc, DateTime nowUtc)
    {
        var age = nowUtc - reauthenticatedAtUtc;
        return age < TimeSpan.Zero || age > MaxAge
            ? Result.Failure(CredentialAdministrationErrors.StaleReauthentication)
            : Result.Success();
    }
}
