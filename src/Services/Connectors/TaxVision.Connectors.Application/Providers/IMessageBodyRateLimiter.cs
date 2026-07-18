namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Cupo de body-fetch por (tenant, cuenta) — distinto de <see cref="IProviderRateLimiter"/> (que
/// protege al PROVEEDOR, particiona por ProviderCode y espera el cupo). Este protege al ENDPOINT
/// M2M de Fase 8: falla rápido (nunca espera) porque Correspondence espera &lt;500ms típico.
/// </summary>
public interface IMessageBodyRateLimiter
{
    /// <summary>true si había cupo y se consumió; false si se superó el límite (10/min default) — el caller debe rechazar con 429, no reintentar/esperar acá.</summary>
    Task<bool> TryAcquireAsync(Guid tenantId, Guid accountId, CancellationToken ct = default);
}
