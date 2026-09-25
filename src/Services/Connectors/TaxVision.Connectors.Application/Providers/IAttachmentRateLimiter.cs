namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Cupo de attachment-fetch por (tenant, cuenta) — 30/min default. Antes era 5/min por tenant entero:
/// una oficina abriendo adjuntos en varios buzones compartía un solo cupo. Mismo criterio de partición
/// y fail-fast que <see cref="IMessageBodyRateLimiter"/>.
/// </summary>
public interface IAttachmentRateLimiter
{
    Task<bool> TryAcquireAsync(Guid tenantId, Guid accountId, CancellationToken ct = default);
}
