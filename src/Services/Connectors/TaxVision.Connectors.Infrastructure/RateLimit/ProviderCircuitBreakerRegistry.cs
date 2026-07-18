using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TaxVision.Connectors.Infrastructure.Observability;

namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>
/// Un <see cref="ProviderCircuitBreaker"/> por clave, creado bajo demanda y reusado entre llamadas.
/// La clave debe incluir tanto el <c>ProviderCode</c> como el concern (p. ej. <c>"Gmail:oauth-refresh"</c>
/// vs <c>"Gmail:messages"</c>) — un breaker compartido entre OAuth refresh y fetch de mensajes haría que
/// una tormenta de fallos leyendo el mailbox abra también el breaker que protege el refresh de tokens,
/// mezclando dos fallas de naturaleza distinta.
/// </summary>
public sealed class ProviderCircuitBreakerRegistry(ILogger<ProviderCircuitBreakerRegistry> logger)
{
    private readonly ConcurrentDictionary<string, ProviderCircuitBreaker> _breakers = new();

    public ProviderCircuitBreaker GetOrCreate(string key) =>
        _breakers.GetOrAdd(
            key,
            code =>
                ProviderCircuitBreaker.Create(
                    code,
                    onOpened: opened =>
                    {
                        logger.LogWarning("Circuit breaker opened for {Key}.", opened);
                        ConnectorsMetrics.CircuitBreakerOpened.Add(
                            1,
                            new KeyValuePair<string, object?>("provider", opened)
                        );
                    }
                )
        );
}
