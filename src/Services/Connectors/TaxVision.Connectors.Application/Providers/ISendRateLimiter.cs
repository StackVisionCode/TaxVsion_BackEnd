namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Cupo de envío por (tenant, cuenta) — D3 §3.5. Distinto de <see cref="IProviderRateLimiter"/> (que
/// protege al PROVEEDOR globalmente y espera el cupo): el envío lo dispara una acción real de un
/// usuario del tenant, cuyo volumen puede variar mucho, así que necesita un límite per-cuenta propio.
/// Mismo criterio fail-fast que <see cref="IMessageBodyRateLimiter"/> — nunca espera, el caller rechaza
/// con 429 si no hay cupo.
/// </summary>
public interface ISendRateLimiter
{
    /// <summary>true si había cupo y se consumió; false si se superó el límite (20/min default) — el caller debe rechazar, no reintentar/esperar acá.</summary>
    Task<bool> TryAcquireAsync(Guid tenantId, Guid accountId, CancellationToken ct = default);
}
