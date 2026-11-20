using TaxVision.Mobile.Domain.RateLimiting;

namespace TaxVision.Mobile.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection>
{
    /// <summary>Agrega y guarda; <c>false</c> si otra escritura ya creó la fila del tenant.</summary>
    Task<bool> TryAddAndSaveAsync(TenantPlanCodeProjection projection, CancellationToken ct = default);
}
