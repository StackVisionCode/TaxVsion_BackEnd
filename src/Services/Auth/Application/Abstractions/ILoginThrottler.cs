namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Throttling complementario por IP respaldado por Redis. El lockout autoritativo
/// por cuenta vive en User (FailedLoginCount/LockoutEndUtc).
/// </summary>
public interface ILoginThrottler
{
    /// <summary>Devuelve el tiempo de espera si la IP superó el umbral de fallos; null si puede intentar.</summary>
    Task<TimeSpan?> GetIpRetryAfterAsync(string? ipAddress, CancellationToken ct = default);

    Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default);

    /// <summary>Throttle de reenvío de OTP: true si aún debe esperar.</summary>
    Task<bool> IsOtpResendThrottledAsync(Guid userId, CancellationToken ct = default);

    Task RegisterOtpSentAsync(Guid userId, CancellationToken ct = default);
}
