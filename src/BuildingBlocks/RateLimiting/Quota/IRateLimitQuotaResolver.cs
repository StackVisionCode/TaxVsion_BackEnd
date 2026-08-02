namespace BuildingBlocks.RateLimiting;

/// <summary>
/// Resuelve la cuota efectiva de una política para un tenant concreto, aplicando el
/// multiplicador (o hard-override) de su plan — Plan_Implementacion_Fases.md §5, ADR_017.
/// El middleware de Fase 3 llama esto una vez por request autenticado con partición de tenant.
/// </summary>
public interface IRateLimitQuotaResolver
{
    Task<EffectiveQuota> ResolveAsync(RateLimitPolicyDefinition policy, Guid tenantId, CancellationToken ct = default);
}
