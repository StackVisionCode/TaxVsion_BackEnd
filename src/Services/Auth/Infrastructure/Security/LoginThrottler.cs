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
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan OtpResendWindow = TimeSpan.FromMinutes(1);

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

    private static string FailureKey(string ipAddress) => $"auth:failip:{ipAddress}";

    private static string OtpKey(Guid userId) => $"auth:otp-resend:{userId:N}";
}
