namespace BuildingBlocks.Infrastructure.Security;

/// <summary>
/// F25 — contrato compartido que cada servicio ya implementa localmente (cache vía
/// <see cref="ExpiringValueCache{TKey,TValue}"/> + <see cref="ServiceTokenHttpAcquisition"/>)
/// para obtener tokens M2M por tenant. RateLimit Fase 1 lo hace público para que
/// <c>HttpPlanRateLimitReader</c> (compartido) pueda depender de él sin acoplarse a la clase
/// concreta de ningún servicio en particular.
/// </summary>
public interface IServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}
