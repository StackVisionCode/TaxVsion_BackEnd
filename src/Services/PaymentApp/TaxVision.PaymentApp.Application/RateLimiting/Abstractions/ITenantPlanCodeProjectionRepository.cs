using TaxVision.PaymentApp.Domain.RateLimiting;

namespace TaxVision.PaymentApp.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
