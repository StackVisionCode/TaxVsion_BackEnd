using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace BuildingBlocks.Infrastructure.Resilience;

/// <summary>
/// Retry + circuit breaker Polly compartido (F24 — extraído de las 3 copias que existían en
/// Auth/Connectors/Postmaster). El pipeline reintenta primero (backoff exponencial + jitter, hasta
/// <paramref name="maxRetryAttempts"/> veces) fallos transitorios de red (<see cref="HttpRequestException"/>,
/// <see cref="TaskCanceledException"/> por timeout); si los reintentos se agotan, el circuit breaker
/// cuenta el fallo y abre tras <paramref name="minimumThroughput"/> fallos consecutivos (FailureRatio
/// 1.0), quedando abierto <paramref name="breakDuration"/>. Solo cuenta fallos que el operation
/// envuelto señala lanzando una excepción, nunca por un Result.Failure devuelto sin excepción.
/// <paramref name="onRetry"/>/<paramref name="onOpened"/> son ganchos opcionales para que cada
/// servicio emita bajo su propio Meter/clase de métricas estática — el pipeline no asume ningún
/// Meter compartido.
/// </summary>
public sealed class HttpResiliencePipeline
{
    private readonly ResiliencePipeline _pipeline;

    private HttpResiliencePipeline(ResiliencePipeline pipeline) => _pipeline = pipeline;

    public static HttpResiliencePipeline Create(
        string boundaryName,
        int minimumThroughput = 3,
        TimeSpan? breakDuration = null,
        int maxRetryAttempts = 2,
        Action<string>? onRetry = null,
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
                        onRetry?.Invoke(boundaryName);
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
                        onOpened?.Invoke(boundaryName);
                        return default;
                    },
                }
            )
            .Build();
        return new HttpResiliencePipeline(pipeline);
    }

    /// <summary>Lanza <see cref="BrokenCircuitException"/> sin invocar <paramref name="operation"/> si el circuito está abierto.</summary>
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
        _pipeline.ExecuteAsync(async token => await operation(token), ct).AsTask();
}
