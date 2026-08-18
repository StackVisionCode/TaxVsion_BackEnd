using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

/// <summary>
/// RateLimit Fase 8 — <see cref="BuildingBlocks.Infrastructure.RateLimiting.RateLimitMetrics"/> usa
/// un <see cref="System.Diagnostics.Metrics.Meter"/> con nombre fijo (proceso-wide). Mismo criterio
/// que <c>AuthorizationMetricsCollection</c> (RBAC Fase 10): agrupar acá todas las clases que
/// crean/aserten sobre ese Meter para que xUnit nunca las corra concurrentemente entre sí.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RateLimitMetricsCollection
{
    public const string Name = "RateLimitMetrics (serialized — shared Meter)";
}
