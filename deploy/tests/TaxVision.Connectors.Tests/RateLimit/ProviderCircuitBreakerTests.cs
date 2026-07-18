using Polly.CircuitBreaker;
using TaxVision.Connectors.Infrastructure.RateLimit;

namespace TaxVision.Connectors.Tests.RateLimit;

public class ProviderCircuitBreakerTests
{
    [Fact]
    public async Task ExecuteAsync_WithSuccessfulOperation_ReturnsResultAndStaysClosed()
    {
        var breaker = ProviderCircuitBreaker.Create("gmail", minimumThroughput: 3);

        var result = await breaker.ExecuteAsync(_ => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteAsync_After3ConsecutiveFailures_OpensAndSkipsSubsequentCalls()
    {
        var breaker = ProviderCircuitBreaker.Create(
            "gmail",
            minimumThroughput: 3,
            breakDuration: TimeSpan.FromSeconds(60)
        );
        var attempts = 0;

        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                breaker.ExecuteAsync<int>(_ =>
                {
                    attempts++;
                    throw new InvalidOperationException("provider unavailable");
                })
            );
        }

        Assert.Equal(3, attempts);

        // El circuito ya está abierto — la 4ta llamada NUNCA invoca el operation.
        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            breaker.ExecuteAsync<int>(_ =>
            {
                attempts++;
                return Task.FromResult(0);
            })
        );

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_WithTransientHttpFailureThenSuccess_RetriesAndReturnsResult()
    {
        var breaker = ProviderCircuitBreaker.Create("gmail", minimumThroughput: 3);
        var attempts = 0;

        var result = await breaker.ExecuteAsync<int>(_ =>
        {
            attempts++;
            if (attempts == 1)
                throw new HttpRequestException("transient network failure");
            return Task.FromResult(99);
        });

        Assert.Equal(99, result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonTransientException_DoesNotRetryBeforeCountingFailure()
    {
        var breaker = ProviderCircuitBreaker.Create(
            "gmail",
            minimumThroughput: 3,
            breakDuration: TimeSpan.FromSeconds(60)
        );
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            breaker.ExecuteAsync<int>(_ =>
            {
                attempts++;
                throw new InvalidOperationException("not a transient network failure");
            })
        );

        // InvalidOperationException no matchea el ShouldHandle del retry (solo HttpRequestException/
        // TaskCanceledException) — un único intento, sin reintentos, antes de contar el fallo.
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void GetOrCreate_WithSameProviderCode_ReturnsSameBreakerInstance()
    {
        var registry = new ProviderCircuitBreakerRegistry(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderCircuitBreakerRegistry>.Instance
        );

        var first = registry.GetOrCreate("gmail");
        var second = registry.GetOrCreate("gmail");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_WithDifferentProviderCodes_ReturnsDistinctBreakerInstances()
    {
        var registry = new ProviderCircuitBreakerRegistry(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProviderCircuitBreakerRegistry>.Instance
        );

        var gmail = registry.GetOrCreate("gmail");
        var graph = registry.GetOrCreate("graph");

        Assert.NotSame(gmail, graph);
    }
}
