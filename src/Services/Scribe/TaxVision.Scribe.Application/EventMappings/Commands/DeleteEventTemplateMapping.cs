using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Application.EventMappings.Commands;

public sealed record DeleteEventTemplateMappingCommand(Guid Id, Guid? TenantId, bool IsPlatformAdmin);

public static class DeleteEventTemplateMappingHandler
{
    public static async Task<Result> Handle(
        DeleteEventTemplateMappingCommand command,
        IEventTemplateMappingRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var existingResult = await repository.GetByIdAsync(command.Id, ct);
        if (existingResult.IsFailure)
            return Result.Failure(existingResult.Error);

        var mapping = existingResult.Value;

        if (mapping.Scope == TemplateScope.System && !command.IsPlatformAdmin)
            return Result.Failure(
                new Error("EventTemplateMapping.Forbidden", "Only platform administrators can manage system mappings.")
            );

        if (mapping.Scope == TemplateScope.Tenant && mapping.TenantId != command.TenantId && !command.IsPlatformAdmin)
            return Result.Failure(
                new Error("EventTemplateMapping.Forbidden", "This mapping does not belong to your tenant.")
            );

        await repository.RemoveAsync(command.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
