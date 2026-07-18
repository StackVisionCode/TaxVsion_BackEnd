using Polly;
using Polly.CircuitBreaker;

namespace TaxVision.Postmaster.Infrastructure.RateLimit;

/// <summary>
/// Circuit breaker Polly por provider — abre tras <see cref="MinimumThroughput"/> fallos consecutivos
/// (FailureRatio 1.0) y se mantiene abierto <see cref="BreakDuration"/>. Envuelto en su propia clase
/// (en vez de vivir inline en <c>SmtpEmailSender</c>) para poder testear la máquina de estados sin
/// I/O real de SMTP.
/// </summary>
public sealed class ProviderCircuitBreaker
{
    private readonly ResiliencePipeline _pipeline;

    private ProviderCircuitBreaker(ResiliencePipeline pipeline) => _pipeline = pipeline;

    public static ProviderCircuitBreaker Create(
        string providerCode,
        int minimumThroughput = 5,
        TimeSpan? breakDuration = null,
        Action<string>? onOpened = null
    )
    {
        var pipeline = new ResiliencePipelineBuilder()
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
