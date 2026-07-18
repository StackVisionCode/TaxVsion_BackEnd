using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using TaxVision.Connectors.Infrastructure.Observability;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>
/// Retry + circuit breaker Polly por provider (Fase 10 — retries centralizados). El pipeline reintenta
/// primero (backoff exponencial + jitter, hasta <see cref="maxRetryAttempts"/> veces) fallos
/// transitorios de red (<see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/> por
/// timeout); si los reintentos se agotan, el circuit breaker cuenta el fallo y abre tras
/// <see cref="minimumThroughput"/> fallos consecutivos (FailureRatio 1.0), quedando abierto
/// <see cref="breakDuration"/>. Solo cuenta fallos que el operation envuelto señala lanzando una
/// excepción (ver OAuthProviderException/EmailProviderException) — nunca por un Result.Failure
/// devuelto sin excepción. El 429-con-Retry-After de Gmail/Graph se maneja aparte (es información de
/// proveedor, no un fallo transitorio genérico) — ver GmailApiClient/GraphApiClient.
/// </summary>
public sealed class ProviderCircuitBreaker
{
    private readonly ResiliencePipeline _pipeline;

    private ProviderCircuitBreaker(ResiliencePipeline pipeline) => _pipeline = pipeline;

    public static ProviderCircuitBreaker Create(
        string providerCode,
        int minimumThroughput = 3,
        TimeSpan? breakDuration = null,
        int maxRetryAttempts = 2,
        Action<string>? onOpened = null
    )
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(
                new RetryStrategyOptions
                {
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<TaskCanceledException>(),
                    MaxRetryAttempts = maxRetryAttempts,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(500),
                    UseJitter = true,
                    OnRetry = _ =>
                    {
                        ConnectorsMetrics.RetryAttempts.Add(
                            1,
                            new KeyValuePair<string, object?>("provider", providerCode)
                        );
                        return default;
                    },
                }
            )
            .AddCircuitBreaker(
                new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 1.0,
                    MinimumThroughput = minimumThroughput,
                    SamplingDuration = TimeSpan.FromMinutes(2),
                    BreakDuration = breakDuration ?? TimeSpan.FromSeconds(60),
                    OnOpened = _ =>
                    {
                        onOpened?.Invoke(providerCode);
                        return default;
                    },
                }
            )
            .Build();
        return new ProviderCircuitBreaker(pipeline);
    }

    /// <summary>Lanza <see cref="BrokenCircuitException"/> sin invocar <paramref name="operation"/> si el circuito está abierto.</summary>
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
        _pipeline.ExecuteAsync(async token => await operation(token), ct).AsTask();
}
