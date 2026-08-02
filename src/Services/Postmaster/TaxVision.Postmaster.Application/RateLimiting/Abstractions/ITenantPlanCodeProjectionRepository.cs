using TaxVision.Postmaster.Domain.RateLimiting;

namespace TaxVision.Postmaster.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
