using BuildingBlocks.TimeZones;

namespace BuildingBlocks.TenantTimeZones;

/// <summary>Zona de una oficina leída de la proyección local, sin llamadas HTTP por render.</summary>
public interface ITenantTimeZoneResolver
{
    /// <summary>Sin proyección o con zona inválida devuelve UTC con <c>IsFallback</c> y lo registra.</summary>
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Descarta la zona en caché de esa oficina (la llama el consumer al proyectar un cambio).</summary>
    void Forget(Guid tenantId);
}
