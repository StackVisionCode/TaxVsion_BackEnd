namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Cupo de attachment-fetch por tenant (5/min default, Fase 9) — particionado solo por tenant, no
/// por cuenta (a diferencia de <see cref="IMessageBodyRateLimiter"/>). Fail-fast, igual criterio.
/// </summary>
public interface IAttachmentRateLimiter
{
    Task<bool> TryAcquireAsync(Guid tenantId, CancellationToken ct = default);
}
