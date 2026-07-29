using BuildingBlocks.Caching;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Throttling complementario respaldado por Redis (vía ICacheService).
/// El lockout autoritativo por cuenta vive en User (FailedLoginCount/LockoutEndUtc);
/// esto añade defensa por IP y control de reenvío de OTP. El contador no es
/// estrictamente atómico, lo cual es aceptable para este propósito.
/// </summary>
public sealed class LoginThrottler(ICacheService cache) : ILoginThrottler
{
    private const int MaxIpFailures = 20;
    private const int MaxPasswordResetRequestsPerEmail = 3;
    private const int MaxPasswordResetRequestsPerIp = 10;
    private const int MaxInvitationAcceptAttemptsPerIp = 20;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OtpResendWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PasswordResetWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan PasswordResetCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InvitationAcceptWindow = TimeSpan.FromHours(1);

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
}
