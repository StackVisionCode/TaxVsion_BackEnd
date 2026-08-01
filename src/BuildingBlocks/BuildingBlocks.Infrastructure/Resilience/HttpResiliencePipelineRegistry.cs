using System.Collections.Concurrent;

namespace BuildingBlocks.Infrastructure.Resilience;

/// <summary>
/// Un <see cref="HttpResiliencePipeline"/> por clave, creado bajo demanda y reusado entre llamadas
/// (singleton: el circuit breaker necesita estado compartido entre llamadas, los HttpClients tipados
/// suelen ser transient). Los callbacks de métricas/logging se bindean una sola vez en el constructor
/// — cada servicio registra su propia instancia de este registry con sus propios callbacks, cerrando
/// sobre su Meter/logger.
/// </summary>
public sealed class HttpResiliencePipelineRegistry(
    int minimumThroughput = 3,
    TimeSpan? breakDuration = null,
    int maxRetryAttempts = 2,
    Action<string>? onRetry = null,
    Action<string>? onOpened = null
)
{
    private readonly ConcurrentDictionary<string, HttpResiliencePipeline> _pipelines = new();

    public HttpResiliencePipeline GetOrCreate(string key)
    {
        var pipelineKey = ResiliencePipelineKey.From(key);
        return _pipelines.GetOrAdd(
            pipelineKey.Value,
            k => HttpResiliencePipeline.Create(k, minimumThroughput, breakDuration, maxRetryAttempts, onRetry, onOpened)
        );
    }
}
