using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace TaxVision.Auth.Infrastructure.Onboarding.Resilience;

/// <summary>
/// PayFlow (auditoría F06) — retry + circuit breaker Polly por cliente M2M, mismo diseño que
/// <c>Connectors.Infrastructure.RateLimit.ProviderCircuitBreaker</c> (Fase 10 de Connectors): 2
/// reintentos con backoff exponencial + jitter para fallos transitorios de red
/// (<see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/>), seguidos de un circuit
/// breaker (FailureRatio 1.0, abre tras 3 fallos consecutivos en 2 min, permanece abierto 30s) para no
/// insistir contra un downstream caído. Antes de este fix los 5 HttpClients M2M de Onboarding
/// (Documents/Tenant/Subscription/PaymentApp/Auth-loopback) usaban <c>HttpClient.SendAsync</c> desnudo
/// — un único timeout/hiccup transitorio del downstream fallaba el paso de la Saga completo, delegando
/// toda la recuperación al <c>OnboardingRetryScheduler</c> de cadencia >=1 minuto.
/// </summary>
public sealed class OnboardingHttpResiliencePipeline
{
    private readonly ResiliencePipeline _pipeline;

    private OnboardingHttpResiliencePipeline(ResiliencePipeline pipeline) => _pipeline = pipeline;

    public static OnboardingHttpResiliencePipeline Create(string clientName) =>
        new(
            new ResiliencePipelineBuilder()
                .AddRetry(
                    new RetryStrategyOptions
                    {
                        ShouldHandle = new PredicateBuilder()
                            .Handle<HttpRequestException>()
                            .Handle<TaskCanceledException>(),
                        MaxRetryAttempts = 2,
                        BackoffType = DelayBackoffType.Exponential,
                        Delay = TimeSpan.FromMilliseconds(500),
                        UseJitter = true,
                    }
                )
                .AddCircuitBreaker(
                    new CircuitBreakerStrategyOptions
                    {
                        FailureRatio = 1.0,
                        MinimumThroughput = 3,
                        SamplingDuration = TimeSpan.FromMinutes(2),
                        BreakDuration = TimeSpan.FromSeconds(30),
                    }
                )
                .Build()
        );

    /// <summary>Lanza <see cref="BrokenCircuitException"/> sin invocar <paramref name="operation"/> si el circuito está abierto.</summary>
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
        _pipeline.ExecuteAsync(async token => await operation(token), ct).AsTask();
}
