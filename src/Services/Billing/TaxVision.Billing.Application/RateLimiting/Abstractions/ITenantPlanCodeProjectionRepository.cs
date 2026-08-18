using TaxVision.Billing.Domain.RateLimiting;

namespace TaxVision.Billing.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
