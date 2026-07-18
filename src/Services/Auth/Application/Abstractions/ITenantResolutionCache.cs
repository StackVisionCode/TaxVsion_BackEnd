namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Cache de corta duración (Fase A3) para evitar una consulta a BD por cada request
/// solo para saber a qué tenant apunta un Host ya resuelto antes. Nunca cachea
/// negativos (Host desconocido) — un Host recién creado debe resolver de inmediato
/// en la siguiente request, no esperar a que expire una entrada negativa.
/// </summary>
public interface ITenantResolutionCache
{
    Task<Guid?> TryGetAsync(string host, CancellationToken ct = default);

    Task SetAsync(string host, Guid tenantId, CancellationToken ct = default);

    Task InvalidateAsync(string host, CancellationToken ct = default);
}
