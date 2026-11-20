using BuildingBlocks.Infrastructure.RateLimiting;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Security;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// Tope del login central por email, contra el Redis local (user-secrets de Auth.Api). Cada prueba usa un email
/// propio, así no comparte contador con otra corrida.
/// </summary>
public sealed class LoginThrottlerEmailIntegrationTests : IAsyncLifetime
{
    private ConnectionMultiplexer _redis = null!;
    private LoginThrottler _throttler = null!;
    private readonly string _email = $"throttle-{Guid.NewGuid():N}@example.com";

    private static string RedisConnectionString =>
        new ConfigurationBuilder()
            .AddUserSecrets("c9f518b8-3374-4abd-a7b6-f0b20cb0877f")
            .Build()
            .GetConnectionString("Redis")
        ?? "localhost:6379";

    public async Task InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
        _throttler = new LoginThrottler(_redis, new RedisRateCounter(_redis));
    }

    public async Task DisposeAsync()
    {
        await _throttler.ResetEmailFailuresAsync(_email, null);
        await _redis.DisposeAsync();
    }

    private async Task FailAsync(int times, UserAccountKind? kind)
    {
        for (var attempt = 0; attempt < times; attempt++)
            await _throttler.RegisterEmailFailureAsync(_email, kind);
    }

    // Decisión del dueño (2026-11-19): 10 fallos, y la espera baja de 15 a 5 minutos.
    [Fact]
    public async Task Ten_failures_make_the_email_wait_at_most_five_minutes()
    {
        await FailAsync(LoginThrottler.MaxEmailFailures - 1, UserAccountKind.Portal);
        Assert.Null(await _throttler.GetEmailRetryAfterAsync(_email, UserAccountKind.Portal));

        await FailAsync(1, UserAccountKind.Portal);

        var wait = await _throttler.GetEmailRetryAfterAsync(_email, UserAccountKind.Portal);
        Assert.NotNull(wait);
        Assert.InRange(wait.Value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task Failures_on_the_client_portal_do_not_block_the_same_email_as_staff()
    {
        await FailAsync(LoginThrottler.MaxEmailFailures, UserAccountKind.Portal);

        Assert.Null(await _throttler.GetEmailRetryAfterAsync(_email, UserAccountKind.Staff));
        Assert.NotNull(await _throttler.GetEmailRetryAfterAsync(_email, null));
    }

    [Fact]
    public async Task A_good_sign_in_clears_the_wait()
    {
        await FailAsync(LoginThrottler.MaxEmailFailures, null);

        await _throttler.ResetEmailFailuresAsync(_email, null);

        Assert.Null(await _throttler.GetEmailRetryAfterAsync(_email, UserAccountKind.Staff));
        Assert.Null(await _throttler.GetEmailRetryAfterAsync(_email, UserAccountKind.Portal));
    }

    [Fact]
    public async Task The_email_is_compared_without_case_or_spaces()
    {
        await FailAsync(LoginThrottler.MaxEmailFailures, UserAccountKind.Staff);

        Assert.NotNull(
            await _throttler.GetEmailRetryAfterAsync($"  {_email.ToUpperInvariant()} ", UserAccountKind.Staff)
        );
    }
}
