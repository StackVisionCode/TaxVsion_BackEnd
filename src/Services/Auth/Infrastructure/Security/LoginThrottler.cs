using BuildingBlocks.Caching;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Throttling complementario respaldado por Redis (vía ICacheService).
/// El lockout autoritativo por cuenta vive en User (FailedLoginCount/LockoutEndUtc);
/// esto añade defensa por IP y control de reenvío de OTP. El contador no es
/// estrictamente atómico, lo cual es aceptable para este propósito.
/// <para>
/// Auditoría F08 — los métodos <c>AuthorizeOnboarding*</c> vinieron de <c>RedisOnboardingOtpThrottler</c>
/// (eliminado); mismas claves Redis (<c>auth:onboarding:otp-create:*</c>/<c>auth:onboarding:otp-resend:*</c>)
/// para no invalidar cooldowns en vuelo al desplegar este cambio.
/// </para>
/// </summary>
public sealed class LoginThrottler(ICacheService cache) : ILoginThrottler
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

        var count = await cache.GetAsync<int?>(FailureKey(ipAddress), ct);
        return count >= MaxIpFailures ? FailureWindow : null;
    }

    public async Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return;

        var key = FailureKey(ipAddress);
        var count = await cache.GetAsync<int?>(key, ct) ?? 0;
        await cache.SetAsync(key, count + 1, FailureWindow, ct);
    }

    public async Task<bool> IsOtpResendThrottledAsync(Guid userId, CancellationToken ct = default) =>
        await cache.GetAsync<bool?>(OtpKey(userId), ct) == true;

    public Task RegisterOtpSentAsync(Guid userId, CancellationToken ct = default) =>
        cache.SetAsync(OtpKey(userId), true, OtpResendWindow, ct);

    public async Task<TimeSpan?> GetPasswordResetRetryAfterAsync(
        string email,
        string? ipAddress,
        CancellationToken ct = default
    )
    {
        if (await cache.GetAsync<bool?>(PasswordResetCooldownKey(email), ct) == true)
            return PasswordResetCooldown;

        var emailCount = await cache.GetAsync<int?>(PasswordResetEmailKey(email), ct) ?? 0;
        if (emailCount >= MaxPasswordResetRequestsPerEmail)
            return PasswordResetWindow;

        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            var ipCount = await cache.GetAsync<int?>(PasswordResetIpKey(ipAddress), ct) ?? 0;
            if (ipCount >= MaxPasswordResetRequestsPerIp)
                return PasswordResetWindow;
        }

        return null;
    }

    public async Task RegisterPasswordResetRequestAsync(string email, string? ipAddress, CancellationToken ct = default)
    {
        await cache.SetAsync(PasswordResetCooldownKey(email), true, PasswordResetCooldown, ct);

        var emailKey = PasswordResetEmailKey(email);
        var emailCount = await cache.GetAsync<int?>(emailKey, ct) ?? 0;
        await cache.SetAsync(emailKey, emailCount + 1, PasswordResetWindow, ct);

        if (string.IsNullOrWhiteSpace(ipAddress))
            return;

        var ipKey = PasswordResetIpKey(ipAddress);
        var ipCount = await cache.GetAsync<int?>(ipKey, ct) ?? 0;
        await cache.SetAsync(ipKey, ipCount + 1, PasswordResetWindow, ct);
    }

    public async Task<TimeSpan?> GetInvitationAcceptRetryAfterAsync(string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        var count = await cache.GetAsync<int?>(InvitationAcceptKey(ipAddress), ct) ?? 0;
        return count >= MaxInvitationAcceptAttemptsPerIp ? InvitationAcceptWindow : null;
    }

    public async Task RegisterInvitationAcceptAttemptAsync(string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return;

        var key = InvitationAcceptKey(ipAddress);
        var count = await cache.GetAsync<int?>(key, ct) ?? 0;
        await cache.SetAsync(key, count + 1, InvitationAcceptWindow, ct);
    }

    private static string FailureKey(string ipAddress) => $"auth:failip:{ipAddress}";

    private static string OtpKey(Guid userId) => $"auth:otp-resend:{userId:N}";

    private static string PasswordResetCooldownKey(string email) => $"auth:pwreset-cooldown:{email}";

    private static string PasswordResetEmailKey(string email) => $"auth:pwreset-email:{email}";

    private static string PasswordResetIpKey(string ipAddress) => $"auth:pwreset-ip:{ipAddress}";

    private static string InvitationAcceptKey(string ipAddress) => $"auth:invite-accept-ip:{ipAddress}";

    public async Task<Result> AuthorizeOnboardingChallengeCreationAsync(
        string email,
        string ipAddress,
        CancellationToken ct = default
    )
    {
        var emailKey = OnboardingChallengeEmailKey(email);
        var emailCount = (await cache.GetAsync<int?>(emailKey, ct) ?? 0) + 1;
        await cache.SetAsync(emailKey, emailCount, OnboardingChallengeCreationWindow, ct);
        if (emailCount > MaxOnboardingChallengesPerEmailPerHour)
            return Result.Failure(
                new Error(
                    "Onboarding.OtpRateLimited",
                    "Too many verification requests for this email. Try again later."
                )
            );

        var ipKey = OnboardingChallengeIpKey(ipAddress);
        var ipCount = (await cache.GetAsync<int?>(ipKey, ct) ?? 0) + 1;
        await cache.SetAsync(ipKey, ipCount, OnboardingChallengeCreationWindow, ct);
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
        var alreadySentRecently = await cache.GetAsync<bool?>(key, ct);
        if (alreadySentRecently == true)
            return Result.Failure(
                new Error("Onboarding.ResendCooldown", "Please wait before requesting another code.")
            );

        await cache.SetAsync(key, true, OnboardingResendCooldown, ct);
        return Result.Success();
    }

    private static string OnboardingChallengeEmailKey(string email) => $"auth:onboarding:otp-create:email:{email}";

    private static string OnboardingChallengeIpKey(string ipAddress) => $"auth:onboarding:otp-create:ip:{ipAddress}";

    private static string OnboardingResendKey(Guid challengeId) => $"auth:onboarding:otp-resend:{challengeId:N}";
}
