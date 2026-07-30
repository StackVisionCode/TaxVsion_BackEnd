using BuildingBlocks.Results;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Throttling complementario por IP respaldado por Redis. El lockout autoritativo
/// por cuenta vive en User (FailedLoginCount/LockoutEndUtc).
/// <para>
/// Auditoría F08 — los 2 métodos <c>AuthorizeOnboarding*</c> de abajo absorbieron lo que antes era
/// la interfaz separada <c>IOnboardingOtpThrottler</c>: mismo patrón Redis email+IP/challengeId que
/// el resto de este archivo, y onboarding es pre-registro (no hay <c>userId</c> todavía, a
/// diferencia de <see cref="IsOtpResendThrottledAsync"/>/<see cref="RegisterOtpSentAsync"/> que sí
/// son OTP de MFA de un usuario ya existente) — por eso son métodos nuevos, no overloads.
/// </para>
/// </summary>
public interface ILoginThrottler
{
    /// <summary>Devuelve el tiempo de espera si la IP superó el umbral de fallos; null si puede intentar.</summary>
    Task<TimeSpan?> GetIpRetryAfterAsync(string? ipAddress, CancellationToken ct = default);

    Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default);

    /// <summary>Throttle de reenvío de OTP: true si aún debe esperar.</summary>
    Task<bool> IsOtpResendThrottledAsync(Guid userId, CancellationToken ct = default);

    Task RegisterOtpSentAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Devuelve el tiempo de espera si el email o la IP superaron el umbral de solicitudes
    /// de reset de password (3/email/hora, 10/IP/hora, cooldown 60s); null si puede intentar.</summary>
    Task<TimeSpan?> GetPasswordResetRetryAfterAsync(string email, string? ipAddress, CancellationToken ct = default);

    Task RegisterPasswordResetRequestAsync(string email, string? ipAddress, CancellationToken ct = default);

    /// <summary>Devuelve el tiempo de espera si la IP superó el umbral de canjes de invitación
    /// intentados (20/hora); null si puede intentar.</summary>
    Task<TimeSpan?> GetInvitationAcceptRetryAfterAsync(string? ipAddress, CancellationToken ct = default);

    Task RegisterInvitationAcceptAttemptAsync(string? ipAddress, CancellationToken ct = default);

    /// <summary>Fail-closed: rechaza si el email o la IP superaron el umbral de creación de retos de
    /// verificación de onboarding (5/email/hora, 10/IP/hora).</summary>
    Task<Result> AuthorizeOnboardingChallengeCreationAsync(
        string email,
        string ipAddress,
        CancellationToken ct = default
    );

    /// <summary>Fail-closed: rechaza un reenvío de OTP de onboarding si el challenge ya envió uno en
    /// los últimos 60s.</summary>
    Task<Result> AuthorizeOnboardingResendAsync(Guid challengeId, CancellationToken ct = default);
}
