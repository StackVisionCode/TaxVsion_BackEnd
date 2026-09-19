using TaxVision.Campaigns.Domain.RateLimiting;

namespace TaxVision.Campaigns.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
