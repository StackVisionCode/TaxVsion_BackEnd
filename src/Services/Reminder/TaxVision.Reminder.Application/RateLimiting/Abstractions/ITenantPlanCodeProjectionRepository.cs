using TaxVision.Reminder.Domain.RateLimiting;

namespace TaxVision.Reminder.Application.RateLimiting.Abstractions;

public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
