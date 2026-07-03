using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Modules.Dtos;

namespace TaxVision.Subscription.Application.Abstractions;

public interface IModuleReadService
{
    Task<List<ModuleDto>> GetAllAsync(bool? isActive, Guid? planId, CancellationToken ct = default);
    Task<Result<ModuleDto>> GetByIdWithDetailsAsync(Guid moduleId, CancellationToken ct = default);
}
