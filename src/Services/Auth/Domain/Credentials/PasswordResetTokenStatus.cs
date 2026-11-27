namespace TaxVision.Auth.Domain.Credentials;

/// <summary>Estado derivado del link de reset (no es columna): ver <see cref="PasswordResetToken.StatusAt"/>.</summary>
public enum PasswordResetTokenStatus
{
    Pending,
    Completed,
    Expired,
    Exhausted,
    Superseded,
    Cancelled,
    PasswordChanged,
    AccountSecured,
}
