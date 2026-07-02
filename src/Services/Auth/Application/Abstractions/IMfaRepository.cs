using TaxVision.Auth.Domain.Mfa;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Acceso a datos del segundo factor (MFA): métodos, desafíos de login,
/// códigos de recuperación, dispositivos de confianza y política del tenant.
/// </summary>
public interface IMfaRepository
{
    Task<IReadOnlyList<MfaMethod>> GetMethodsAsync(Guid userId, CancellationToken ct = default);
    Task<MfaMethod?> GetMethodAsync(Guid userId, MfaMethodType type, CancellationToken ct = default);
    Task<MfaMethod?> GetMethodByIdAsync(Guid methodId, CancellationToken ct = default);
    Task AddMethodAsync(MfaMethod method, CancellationToken ct = default);
    void RemoveMethod(MfaMethod method);

    Task AddChallengeAsync(MfaChallenge challenge, CancellationToken ct = default);
    /// <summary>Recupera el desafío MFA pendiente asociado al hash del ticket de login.</summary>
    Task<MfaChallenge?> GetChallengeByTicketHashAsync(string ticketHash, CancellationToken ct = default);

    Task<IReadOnlyList<RecoveryCode>> GetRecoveryCodesAsync(Guid userId, CancellationToken ct = default);
    Task AddRecoveryCodesAsync(IEnumerable<RecoveryCode> codes, CancellationToken ct = default);
    void RemoveRecoveryCodes(IEnumerable<RecoveryCode> codes);

    /// <summary>Busca un dispositivo de confianza por el hash de su token, para saltar el MFA en accesos recordados.</summary>
    Task<TrustedDevice?> GetTrustedDeviceByHashAsync(string deviceTokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<TrustedDevice>> GetTrustedDevicesAsync(Guid userId, CancellationToken ct = default);
    Task AddTrustedDeviceAsync(TrustedDevice device, CancellationToken ct = default);

    /// <summary>Obtiene la política de MFA del tenant que define para quién es obligatorio el segundo factor.</summary>
    Task<TenantMfaPolicy?> GetPolicyAsync(Guid tenantId, CancellationToken ct = default);
    Task AddPolicyAsync(TenantMfaPolicy policy, CancellationToken ct = default);
}
