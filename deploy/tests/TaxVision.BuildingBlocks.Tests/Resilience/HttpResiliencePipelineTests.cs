using BuildingBlocks.Infrastructure.Resilience;
using Polly.CircuitBreaker;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Resilience;

public class HttpResiliencePipelineTests
{
    [Fact]
    public async Task ExecuteAsync_WithSuccessfulOperation_ReturnsResultAndStaysClosed()
    {
        var breaker = HttpResiliencePipeline.Create("gmail", minimumThroughput: 3);

        var result = await breaker.ExecuteAsync(_ => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task ExecuteAsync_After3ConsecutiveFailures_OpensAndSkipsSubsequentCalls()
    {
        var breaker = HttpResiliencePipeline.Create(
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
        var breaker = HttpResiliencePipeline.Create("gmail", minimumThroughput: 3);
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
        var breaker = HttpResiliencePipeline.Create(
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
    public void GetOrCreate_WithSameKey_ReturnsSamePipelineInstance()
    {
        var registry = new HttpResiliencePipelineRegistry();

        var first = registry.GetOrCreate("gmail");
        var second = registry.GetOrCreate("gmail");

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_WithDifferentKeys_ReturnsDistinctPipelineInstances()
    {
        var registry = new HttpResiliencePipelineRegistry();

        var gmail = registry.GetOrCreate("gmail");
        var graph = registry.GetOrCreate("graph");

        Assert.NotSame(gmail, graph);
    }

    [Fact]
    public async Task ExecuteAsync_OnRetryAndOnOpenedCallbacks_AreInvokedWithBoundaryName()
    {
        var retriedKeys = new List<string>();
        var openedKeys = new List<string>();
        var breaker = HttpResiliencePipeline.Create(
            "gmail",
            minimumThroughput: 3,
            breakDuration: TimeSpan.FromSeconds(60),
            onRetry: key => retriedKeys.Add(key),
            onOpened: key => openedKeys.Add(key)
        );

        await breaker
            .ExecuteAsync<int>(_ =>
            {
                throw new HttpRequestException("transient");
            })
            .ContinueWith(_ => { }); // primer intento agota los 2 reintentos y cuenta como 1 fallo

        for (var i = 0; i < 2; i++)
        {
            await breaker
                .ExecuteAsync<int>(_ => throw new InvalidOperationException("non-transient"))
                .ContinueWith(_ => { });
        }

        Assert.Contains("gmail", retriedKeys);
        Assert.Contains("gmail", openedKeys);
    }
}
