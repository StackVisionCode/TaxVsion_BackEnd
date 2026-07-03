using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Plans.Dtos;

namespace TaxVision.Subscription.Application.Plans.Queries;

public sealed record GetAllPlansQuery(bool? IsActive = null);
public sealed record GetPlanByIdQuery(Guid PlanId);

public static class GetAllPlansHandler
{
    public static Task<List<PlanDto>> Handle(
        GetAllPlansQuery query,
        IPlanReadService readService,
        CancellationToken ct)
        => readService.GetAllAsync(query.IsActive, ct);
}

public static class GetPlanByIdHandler
{
    public static Task<Result<PlanDto>> Handle(
        GetPlanByIdQuery query,
        IPlanReadService readService,
        CancellationToken ct)
        => readService.GetByIdWithDetailsAsync(query.PlanId, ct);
}
