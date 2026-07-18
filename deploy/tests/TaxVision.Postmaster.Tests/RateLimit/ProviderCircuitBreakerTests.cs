using Polly.CircuitBreaker;
using TaxVision.Postmaster.Infrastructure.RateLimit;

namespace TaxVision.Postmaster.Tests.RateLimit;

public sealed class ProviderCircuitBreakerTests
{
    [Fact]
    public async Task ExecuteAsync_opens_after_minimum_throughput_consecutive_failures_and_fires_callback()
    {
        var openedFor = new List<string>();
        var breaker = ProviderCircuitBreaker.Create(
            "smtp-provider",
            minimumThroughput: 5,
            breakDuration: TimeSpan.FromSeconds(60),
            onOpened: p => openedFor.Add(p)
        );

        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(
                    _ => throw new InvalidOperationException("simulated 429"),
                    CancellationToken.None
                )
            );
        }

        Assert.Single(openedFor);
        Assert.Equal("smtp-provider", openedFor[0]);

        var invoked = false;
        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            breaker.ExecuteAsync<int>(
                _ =>
                {
                    invoked = true;
                    return Task.FromResult(1);
                },
                CancellationToken.None
            )
        );
        Assert.False(invoked);
    }

    [Fact]
    public async Task ExecuteAsync_stays_closed_when_failures_are_below_minimum_throughput()
    {
        var openedFor = new List<string>();
        var breaker = ProviderCircuitBreaker.Create(
            "smtp-provider",
            minimumThroughput: 5,
            onOpened: p => openedFor.Add(p)
        );

        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(
                    _ => throw new InvalidOperationException("simulated failure"),
                    CancellationToken.None
                )
            );
        }

        var result = await breaker.ExecuteAsync(_ => Task.FromResult(42), CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Empty(openedFor);
    }

    [Fact]
    public async Task ExecuteAsync_returns_operation_result_when_circuit_is_closed()
    {
        var breaker = ProviderCircuitBreaker.Create("smtp-provider");

        var result = await breaker.ExecuteAsync(_ => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
    }
}
