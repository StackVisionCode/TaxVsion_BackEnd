using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.EventMappings.Commands;

public sealed record UpdateEventTemplateMappingCommand(
    Guid Id,
    Guid? TenantId,
    bool IsPlatformAdmin,
    string TemplateKey,
    int Priority,
    bool Enabled
);

public static class UpdateEventTemplateMappingHandler
{
    public static async Task<Result<EventTemplateMappingResponse>> Handle(
        UpdateEventTemplateMappingCommand command,
        IEventTemplateMappingRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var existingResult = await repository.GetByIdAsync(command.Id, ct);
        if (existingResult.IsFailure)
            return Result.Failure<EventTemplateMappingResponse>(existingResult.Error);

        var mapping = existingResult.Value;

        if (mapping.Scope == TemplateScope.System && !command.IsPlatformAdmin)
            return Result.Failure<EventTemplateMappingResponse>(
                new Error("EventTemplateMapping.Forbidden", "Only platform administrators can manage system mappings.")
            );

        if (mapping.Scope == TemplateScope.Tenant && mapping.TenantId != command.TenantId && !command.IsPlatformAdmin)
            return Result.Failure<EventTemplateMappingResponse>(
                new Error("EventTemplateMapping.Forbidden", "This mapping does not belong to your tenant.")
            );

        var templateKeyResult = TemplateKey.Create(command.TemplateKey);
        if (templateKeyResult.IsFailure)
            return Result.Failure<EventTemplateMappingResponse>(templateKeyResult.Error);

        var rebindResult = mapping.Rebind(templateKeyResult.Value, command.Priority, command.Enabled, DateTime.UtcNow);
        if (rebindResult.IsFailure)
            return Result.Failure<EventTemplateMappingResponse>(rebindResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EventTemplateMappingMapper.ToResponse(mapping));
    }
}
