namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Implementación por default de <see cref="ITenantPlanCodeReader"/> — siempre "no sé", nunca I/O.
/// Registrada por <c>AddTieredRateLimiting()</c> (BuildingBlocks.Web) hasta que Fase 6 conecte un
/// lector real por servicio (M2M+caché o proyección local). Con este lector, todo request cae en
/// el camino de fail-open del resolver (<see cref="EffectiveQuota.IsFallback"/> = true, cupo base
/// sin escalar) — comportamiento intencional de Fase 3/5: el middleware pilota con cupos base, el
/// tier-awareness real se activa recién en Fase 6.
/// </summary>
public sealed class NullTenantPlanCodeReader : ITenantPlanCodeReader
{
    public Task<string?> GetPlanCodeAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
