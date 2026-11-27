namespace TaxVision.Auth.Domain.Credentials;

/// <summary>Por qué un link de reset dejó de servir sin usarse.</summary>
public enum ResetRevocationReason : byte
{
    PasswordChanged = 1,
    Superseded = 2,
    CancelledByOffice = 3,
    AccountSecured = 4,
}
