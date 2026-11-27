namespace TaxVision.Auth.Domain.Users;

/// <summary>Estado de la credencial por administración de la oficina (03 §4.3).</summary>
public enum CredentialStatus : byte
{
    Active = 0,
    ChangeRequired = 1,
    ResetRequired = 2,
}
