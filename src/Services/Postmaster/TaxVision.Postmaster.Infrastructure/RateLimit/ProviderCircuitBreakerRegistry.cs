using System.Collections.Concurrent;

namespace TaxVision.Postmaster.Infrastructure.RateLimit;

/// <summary>Un <see cref="ProviderCircuitBreaker"/> por ProviderCode, creado bajo demanda y reusado entre envíos.</summary>
public sealed class ProviderCircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, ProviderCircuitBreaker> _breakers = new();

    public ProviderCircuitBreaker GetOrCreate(string providerCode) =>
        _breakers.GetOrAdd(
            providerCode,
            code =>
                ProviderCircuitBreaker.Create(
                    code,
                    onOpened: opened =>
                        PostmasterMetrics.CircuitBreakerOpened.Add(
                            1,
                            new KeyValuePair<string, object?>("provider", opened)
                        )
                )
        );
}
