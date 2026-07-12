using TaxVision.Auth.Domain.Credentials;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Acceso a datos de los tokens de credenciales: reset de contraseña,
/// verificación de correo y verificación de teléfono.
/// </summary>
public interface ICredentialTokenRepository
{
    Task AddPasswordResetAsync(PasswordResetToken token, CancellationToken ct = default);

    /// <summary>Busca un token de reset de contraseña por su hash (los tokens en claro nunca se almacenan).</summary>
    Task<PasswordResetToken?> GetPasswordResetByHashAsync(string tokenHash, CancellationToken ct = default);

    Task AddEmailVerificationAsync(EmailVerificationToken token, CancellationToken ct = default);

    /// <summary>Busca un token de verificación de correo por su hash.</summary>
    Task<EmailVerificationToken?> GetEmailVerificationByHashAsync(string tokenHash, CancellationToken ct = default);

    Task AddPhoneVerificationAsync(PhoneVerificationToken token, CancellationToken ct = default);

    /// <summary>Obtiene la verificación de teléfono vigente (no usada ni expirada) más reciente del usuario.</summary>
    Task<PhoneVerificationToken?> GetActivePhoneVerificationAsync(Guid userId, CancellationToken ct = default);
}
