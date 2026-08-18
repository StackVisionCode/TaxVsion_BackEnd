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
    private const int MaxIpFailures = 20;
    private const int MaxPasswordResetRequestsPerEmail = 3;
    private const int MaxPasswordResetRequestsPerIp = 10;
    private const int MaxInvitationAcceptAttemptsPerIp = 20;
    private const int MaxOnboardingChallengesPerEmailPerHour = 5;
    private const int MaxOnboardingChallengesPerIpPerHour = 10;
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

        var count = await GetCountAsync(FailureKey(ipAddress));
        return count >= MaxIpFailures ? FailureWindow : null;
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
        if (await GetCountAsync(PasswordResetCooldownKey(email)) > 0)
            return PasswordResetCooldown;

        var emailCount = await GetCountAsync(PasswordResetEmailKey(email));
        if (emailCount >= MaxPasswordResetRequestsPerEmail)
            return PasswordResetWindow;

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            var ipCount = await GetCountAsync(PasswordResetIpKey(ipAddress));
            if (ipCount >= MaxPasswordResetRequestsPerIp)
                return PasswordResetWindow;
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

        var count = await GetCountAsync(InvitationAcceptKey(ipAddress));
        return count >= MaxInvitationAcceptAttemptsPerIp ? InvitationAcceptWindow : null;
    }

    public Task RegisterInvitationAcceptAttemptAsync(string? ipAddress, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(ipAddress)
            ? Task.CompletedTask
            : rateCounter.IncrementAndGetAsync(InvitationAcceptKey(ipAddress), InvitationAcceptWindow, ct);

    private async Task<long> GetCountAsync(RateCounterKey key) =>
        (long)await redis.GetDatabase().StringGetAsync(key.Value);

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
        var emailCount = await rateCounter.IncrementAndGetAsync(
            OnboardingChallengeEmailKey(email),
            OnboardingChallengeCreationWindow,
            ct
        );
        if (emailCount > MaxOnboardingChallengesPerEmailPerHour)
            return Result.Failure(
                new Error(
                    "Onboarding.OtpRateLimited",
                    "Too many verification requests for this email. Try again later."
                )
            );

        var ipCount = await rateCounter.IncrementAndGetAsync(
            OnboardingChallengeIpKey(ipAddress),
            OnboardingChallengeCreationWindow,
            ct
        );
        if (ipCount > MaxOnboardingChallengesPerIpPerHour)
            return Result.Failure(
                new Error(
                    "Onboarding.OtpRateLimited",
                    "Too many verification requests from this address. Try again later."
                )
            );

        return Result.Success();
    }

    public async Task<Result> AuthorizeOnboardingResendAsync(Guid challengeId, CancellationToken ct = default)
    {
        var key = OnboardingResendKey(challengeId);
        var alreadySentRecently = await GetCountAsync(key) > 0;
        if (alreadySentRecently)
            return Result.Failure(
                new Error("Onboarding.ResendCooldown", "Please wait before requesting another code.")
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
