using TaxVision.Auth.Domain.Credentials;

namespace TaxVision.Auth.Domain.Users;

/// <summary>Estado de la credencial puesto por la oficina, con su motivo, cuándo y quién (03 §4.3).</summary>
public sealed record CredentialState(
    CredentialStatus Status,
    CredentialAdministrationReason? Reason,
    DateTime? ChangedAtUtc,
    Guid? ChangedByUserId
)
{
    public static readonly CredentialState Active = new(CredentialStatus.Active, null, null, null);
}

/// <summary>Resultado de pedir un estado: cambió, o ya estaba así (idempotente, sin efectos nuevos).</summary>
public enum CredentialTransition
{
    Changed,
    AlreadyInState,
}
