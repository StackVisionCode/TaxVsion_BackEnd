using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Plans.Dtos;

namespace TaxVision.Subscription.Application.Abstractions;

public interface IPlanReadService
{
    Task<List<PlanDto>> GetAllAsync(bool? isActive, CancellationToken ct = default);
    Task<Result<PlanDto>> GetByIdWithDetailsAsync(Guid planId, CancellationToken ct = default);
}
