using System.Collections.Concurrent;

namespace TaxVision.Auth.Infrastructure.Onboarding.Resilience;

/// <summary>
/// PayFlow (auditoría F06) — cachea un <see cref="OnboardingHttpResiliencePipeline"/> por cliente M2M
/// (singleton: el circuit breaker necesita estado compartido entre llamadas, los HttpClients tipados
/// son transient). Mismo rol que <c>ProviderCircuitBreakerRegistry</c> en Connectors.
/// </summary>
public sealed class OnboardingHttpResiliencePipelineRegistry
{
    private readonly ConcurrentDictionary<string, OnboardingHttpResiliencePipeline> _pipelines = new();

    public OnboardingHttpResiliencePipeline GetOrCreate(string clientName) =>
        _pipelines.GetOrAdd(clientName, OnboardingHttpResiliencePipeline.Create);
}
