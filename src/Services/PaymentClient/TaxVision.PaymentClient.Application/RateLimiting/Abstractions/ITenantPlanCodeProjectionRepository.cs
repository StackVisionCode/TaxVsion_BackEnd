using TaxVision.PaymentClient.Domain.RateLimiting;

namespace TaxVision.PaymentClient.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
