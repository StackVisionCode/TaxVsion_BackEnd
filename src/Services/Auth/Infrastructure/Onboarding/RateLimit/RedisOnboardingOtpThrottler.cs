using BuildingBlocks.Caching;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.RateLimit;

/// <summary>
/// Throttling de OTP de onboarding respaldado por Redis vía ICacheService — mismo patrón que
/// LoginThrottler (no estrictamente atómico, aceptable para este propósito; ver su comentario).
/// Deliberadamente NO usa IConnectionMultiplexer directo (patrón de Connectors/Postmaster):
/// Auth no tiene ese cliente registrado hoy y no vale la pena introducirlo solo para esto.
/// </summary>
public sealed class RedisOnboardingOtpThrottler(ICacheService cache) : IOnboardingOtpThrottler
{
    private const int MaxPerEmailPerHour = 5;
    private const int MaxPerIpPerHour = 10;
    private static readonly TimeSpan CreationWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public async Task<Result> AuthorizeChallengeCreationAsync(
        string email,
        string ipAddress,
        CancellationToken ct = default
    )
    {
        var emailKey = EmailKey(email);
        var emailCount = (await cache.GetAsync<int?>(emailKey, ct) ?? 0) + 1;
        await cache.SetAsync(emailKey, emailCount, CreationWindow, ct);
        if (emailCount > MaxPerEmailPerHour)
            return Result.Failure(
                new Error(
                    "Onboarding.OtpRateLimited",
                    "Too many verification requests for this email. Try again later."
                )
            );

        var ipKey = IpKey(ipAddress);
        var ipCount = (await cache.GetAsync<int?>(ipKey, ct) ?? 0) + 1;
        await cache.SetAsync(ipKey, ipCount, CreationWindow, ct);
        if (ipCount > MaxPerIpPerHour)
            return Result.Failure(
                new Error(
                    "Onboarding.OtpRateLimited",
                    "Too many verification requests from this address. Try again later."
                )
            );

        return Result.Success();
    }

    public async Task<Result> AuthorizeResendAsync(Guid challengeId, CancellationToken ct = default)
    {
        var key = ResendKey(challengeId);
        var alreadySentRecently = await cache.GetAsync<bool?>(key, ct);
        if (alreadySentRecently == true)
            return Result.Failure(
                new Error("Onboarding.ResendCooldown", "Please wait before requesting another code.")
            );

        await cache.SetAsync(key, true, ResendCooldown, ct);
        return Result.Success();
    }

    private static string EmailKey(string email) => $"auth:onboarding:otp-create:email:{email}";

    private static string IpKey(string ipAddress) => $"auth:onboarding:otp-create:ip:{ipAddress}";

    private static string ResendKey(Guid challengeId) => $"auth:onboarding:otp-resend:{challengeId:N}";
}
