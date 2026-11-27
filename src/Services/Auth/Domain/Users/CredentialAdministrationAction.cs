namespace TaxVision.Auth.Domain.Users;

/// <summary>Acción de la oficina sobre la credencial de un usuario (03 §6.4).</summary>
public enum CredentialAdministrationAction
{
    SendResetLink,
    CancelResetLink,
    RequireChange,
    Secure,
    ResetMfa,
    ChangeEmail,
}
