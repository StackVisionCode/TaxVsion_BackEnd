using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Modules.Dtos;

namespace TaxVision.Subscription.Application.Modules.Queries;

public sealed record GetAllModulesQuery(bool? IsActive = null, Guid? PlanId = null);
public sealed record GetModuleByIdQuery(Guid ModuleId);

public static class GetAllModulesHandler
{
    public static Task<List<ModuleDto>> Handle(
        GetAllModulesQuery query,
        IModuleReadService readService,
        CancellationToken ct)
        => readService.GetAllAsync(query.IsActive, query.PlanId, ct);
}

public static class GetModuleByIdHandler
{
    public static Task<Result<ModuleDto>> Handle(
        GetModuleByIdQuery query,
        IModuleReadService readService,
        CancellationToken ct)
        => readService.GetByIdWithDetailsAsync(query.ModuleId, ct);
}
