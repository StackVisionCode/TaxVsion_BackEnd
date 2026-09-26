using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Results;
using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Throttling complementario respaldado por Redis. El lockout autoritativo por cuenta vive en
/// User (FailedLoginCount/LockoutEndUtc); esto añade defensa por IP y control de reenvío de OTP.
/// <para>
/// Rate Limit Fase 0.1 — el incremento de los 9 contadores ahora es atómico vía
/// <see cref="IRateCounter"/> (antes: GET+SET no atómico sobre <c>ICacheService</c>, con
/// lost-updates reales bajo concurrencia — mismo bug de origen que F26 cerró en Connectors/
/// Postmaster/PaymentApp). Dos cambios de comportamiento derivados de esto, ambos aceptados,
/// mismo criterio que <c>PaymentAttemptThrottle</c>:
/// <list type="bullet">
/// <item>Las ventanas pasan de deslizantes (cada intento reseteaba el TTL completo) a fijas (el
/// TTL se fija solo en el primer incremento del ciclo).</item>
/// <item>El check-then-register entre los métodos <c>Get*RetryAfterAsync</c>/<c>Is*ThrottledAsync</c>
/// y sus <c>Register*Async</c> sigue siendo un TOCTOU no atómico — limitación pre-existente
/// conocida, documentada igual desde F08.</item>
/// </list>
/// La lectura de contadores usa <see cref="IConnectionMultiplexer"/> directo (no <c>ICacheService</c>):
/// <see cref="IRateCounter"/> escribe un string Redis crudo vía <c>INCR</c>, formato incompatible
/// con el hash que <c>IDistributedCache</c> espera para sus propias claves.
/// </para>
/// <para>
/// Auditoría F08 — los métodos <c>AuthorizeOnboarding*</c> vinieron de <c>RedisOnboardingOtpThrottler</c>
/// (eliminado); mismas claves Redis (<c>auth:onboarding:otp-create:*</c>/<c>auth:onboarding:otp-resend:*</c>)
/// para no invalidar cooldowns en vuelo al desplegar este cambio.
/// </para>
/// </summary>
public sealed class LoginThrottler(IConnectionMultiplexer redis, IRateCounter rateCounter) : ILoginThrottler
{
    // Por IP de oficina (NAT): con 20 bastaban unos pocos empleados equivocándose para bloquear el login
    // de todos. El brute force de una cuenta lo frena el lockout por cuenta (10 fallos), no esto.
    private const int MaxIpFailures = 50;
    private const int MaxPasswordResetRequestsPerEmail = 3;
    private const int MaxPasswordResetRequestsPerIp = 10;
    private const int MaxInvitationAcceptAttemptsPerIp = 20;
    private const int MaxOnboardingChallengesPerEmailPerHour = 5;

    // Por IP de oficina (NAT): varias altas legítimas comparten IP. El abuso real lo frena el tope por email.
    private const int MaxOnboardingChallengesPerIpPerHour = 30;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OtpResendWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PasswordResetWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan PasswordResetCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InvitationAcceptWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan OnboardingChallengeCreationWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan OnboardingResendCooldown = TimeSpan.FromSeconds(60);

    public async Task<TimeSpan?> GetIpRetryAfterAsync(string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        var key = FailureKey(ipAddress);
        return await GetCountAsync(key) >= MaxIpFailures ? await RemainingAsync(key, FailureWindow) : null;
    }

    public Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(ipAddress)
            ? Task.CompletedTask
            : rateCounter.IncrementAndGetAsync(FailureKey(ipAddress), FailureWindow, ct);

    public async Task<bool> IsOtpResendThrottledAsync(Guid userId, CancellationToken ct = default) =>
        await GetCountAsync(OtpKey(userId)) > 0;

    public Task RegisterOtpSentAsync(Guid userId, CancellationToken ct = default) =>
        rateCounter.IncrementAndGetAsync(OtpKey(userId), OtpResendWindow, ct);

    public async Task<TimeSpan?> GetPasswordResetRetryAfterAsync(
        string email,
        string? ipAddress,
        CancellationToken ct = default
    )
    {
        var cooldownKey = PasswordResetCooldownKey(email);
        if (await GetCountAsync(cooldownKey) > 0)
            return await RemainingAsync(cooldownKey, PasswordResetCooldown);

        var emailKey = PasswordResetEmailKey(email);
        if (await GetCountAsync(emailKey) >= MaxPasswordResetRequestsPerEmail)
            return await RemainingAsync(emailKey, PasswordResetWindow);

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            var ipKey = PasswordResetIpKey(ipAddress);
            if (await GetCountAsync(ipKey) >= MaxPasswordResetRequestsPerIp)
                return await RemainingAsync(ipKey, PasswordResetWindow);
        }

        return null;
    }

    public async Task RegisterPasswordResetRequestAsync(string email, string? ipAddress, CancellationToken ct = default)
    {
        await rateCounter.IncrementAndGetAsync(PasswordResetCooldownKey(email), PasswordResetCooldown, ct);
        await rateCounter.IncrementAndGetAsync(PasswordResetEmailKey(email), PasswordResetWindow, ct);

        if (string.IsNullOrWhiteSpace(ipAddress))
            return;

        await rateCounter.IncrementAndGetAsync(PasswordResetIpKey(ipAddress), PasswordResetWindow, ct);
    }

    public async Task<TimeSpan?> GetInvitationAcceptRetryAfterAsync(string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        var key = InvitationAcceptKey(ipAddress);
        return await GetCountAsync(key) >= MaxInvitationAcceptAttemptsPerIp
            ? await RemainingAsync(key, InvitationAcceptWindow)
            : null;
    }

    public Task RegisterInvitationAcceptAttemptAsync(string? ipAddress, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(ipAddress)
            ? Task.CompletedTask
            : rateCounter.IncrementAndGetAsync(InvitationAcceptKey(ipAddress), InvitationAcceptWindow, ct);

    private async Task<long> GetCountAsync(RateCounterKey key) =>
        (long)await redis.GetDatabase().StringGetAsync(key.Value);

    // La ventana es fija: lo que falta es el TTL de la clave, no la ventana completa.
    private async Task<TimeSpan> RemainingAsync(RateCounterKey key, TimeSpan window) =>
        await redis.GetDatabase().KeyTimeToLiveAsync(key.Value) ?? window;

    private static RateCounterKey FailureKey(string ipAddress) => RateCounterKey.From($"auth:failip:{ipAddress}");

    private static RateCounterKey OtpKey(Guid userId) => RateCounterKey.From($"auth:otp-resend:{userId:N}");

    private static RateCounterKey PasswordResetCooldownKey(string email) =>
        RateCounterKey.From($"auth:pwreset-cooldown:{email}");

    private static RateCounterKey PasswordResetEmailKey(string email) =>
        RateCounterKey.From($"auth:pwreset-email:{email}");

    private static RateCounterKey PasswordResetIpKey(string ipAddress) =>
        RateCounterKey.From($"auth:pwreset-ip:{ipAddress}");

    private static RateCounterKey InvitationAcceptKey(string ipAddress) =>
        RateCounterKey.From($"auth:invite-accept-ip:{ipAddress}");

    public async Task<Result> AuthorizeOnboardingChallengeCreationAsync(
        string email,
        string ipAddress,
        CancellationToken ct = default
    )
    {
        var emailKey = OnboardingChallengeEmailKey(email);
        var emailCount = await rateCounter.IncrementAndGetAsync(emailKey, OnboardingChallengeCreationWindow, ct);
        if (emailCount > MaxOnboardingChallengesPerEmailPerHour)
            return Result.Failure(
                new Error(
                    "Onboarding.OtpRateLimited",
                    "Too many verification requests for this email. Try again later."
                ).WithRetryAfter(await RemainingAsync(emailKey, OnboardingChallengeCreationWindow))
            );

        var ipKey = OnboardingChallengeIpKey(ipAddress);
        var ipCount = await rateCounter.IncrementAndGetAsync(ipKey, OnboardingChallengeCreationWindow, ct);
        if (ipCount > MaxOnboardingChallengesPerIpPerHour)
            return Result.Failure(
                new Error(
                    "Onboarding.OtpRateLimited",
                    "Too many verification requests from this address. Try again later."
                ).WithRetryAfter(await RemainingAsync(ipKey, OnboardingChallengeCreationWindow))
            );

        return Result.Success();
    }

    public async Task<Result> AuthorizeOnboardingResendAsync(Guid challengeId, CancellationToken ct = default)
    {
        var key = OnboardingResendKey(challengeId);
        if (await GetCountAsync(key) > 0)
            return Result.Failure(
                new Error("Onboarding.ResendCooldown", "Please wait before requesting another code.").WithRetryAfter(
                    await RemainingAsync(key, OnboardingResendCooldown)
                )
            );

        await rateCounter.IncrementAndGetAsync(key, OnboardingResendCooldown, ct);
        return Result.Success();
    }

    private static RateCounterKey OnboardingChallengeEmailKey(string email) =>
        RateCounterKey.From($"auth:onboarding:otp-create:email:{email}");

    private static RateCounterKey OnboardingChallengeIpKey(string ipAddress) =>
        RateCounterKey.From($"auth:onboarding:otp-create:ip:{ipAddress}");

    private static RateCounterKey OnboardingResendKey(Guid challengeId) =>
        RateCounterKey.From($"auth:onboarding:otp-resend:{challengeId:N}");
}
