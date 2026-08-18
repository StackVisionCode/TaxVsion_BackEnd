using TaxVision.Inventory.Domain.RateLimiting;

namespace TaxVision.Inventory.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
