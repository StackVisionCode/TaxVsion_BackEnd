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

    /// <summary>Enlaces de reset del usuario que todavía podrían usarse: sin usar, sin anular y sin caducar.</summary>
    Task<IReadOnlyList<PasswordResetToken>> GetPendingPasswordResetsAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken ct = default
    );

    /// <summary>Último link de reset del usuario en cualquier estado: panel de la oficina e idempotencia.</summary>
    Task<PasswordResetToken?> GetLatestPasswordResetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Links pedidos por la oficina (Administrative o Incident) para el usuario desde una fecha.</summary>
    Task<int> CountOfficePasswordResetsSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default);

    Task AddEmailVerificationAsync(EmailVerificationToken token, CancellationToken ct = default);

    /// <summary>Busca un token de verificación de correo por su hash.</summary>
    Task<EmailVerificationToken?> GetEmailVerificationByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Último pedido de cambio de correo del usuario en cualquier estado (idempotencia de D-08).</summary>
    Task<EmailVerificationToken?> GetLatestEmailVerificationAsync(Guid userId, CancellationToken ct = default);

    Task AddPhoneVerificationAsync(PhoneVerificationToken token, CancellationToken ct = default);

    /// <summary>Obtiene la verificación de teléfono vigente (no usada ni expirada) más reciente del usuario.</summary>
    Task<PhoneVerificationToken?> GetActivePhoneVerificationAsync(Guid userId, CancellationToken ct = default);
}
