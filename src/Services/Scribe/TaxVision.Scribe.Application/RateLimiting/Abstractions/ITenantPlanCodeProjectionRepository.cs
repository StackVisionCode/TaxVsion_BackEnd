using TaxVision.Scribe.Domain.RateLimiting;

namespace TaxVision.Scribe.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
