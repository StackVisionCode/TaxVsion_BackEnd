using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.EventMappings;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.EventMappings.Commands;

public sealed record CreateEventTemplateMappingCommand(
    TemplateScope Scope,
    Guid? TenantId,
    bool IsPlatformAdmin,
    string EventKey,
    string TemplateKey,
    string? Locale,
    int Priority
);

public static class CreateEventTemplateMappingHandler
{
    public static async Task<Result<EventTemplateMappingResponse>> Handle(
        CreateEventTemplateMappingCommand command,
        IEventTemplateMappingRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.Scope == TemplateScope.System && !command.IsPlatformAdmin)
            return Result.Failure<EventTemplateMappingResponse>(
                new Error("EventTemplateMapping.Forbidden", "Only platform administrators can manage system mappings.")
            );

        if (command.Scope == TemplateScope.Tenant && (command.TenantId is null || command.TenantId == Guid.Empty))
            return Result.Failure<EventTemplateMappingResponse>(
                new Error("EventTemplateMapping.Tenant", "A tenant context is required for tenant mappings.")
            );

        var eventKeyResult = EventKey.Create(command.EventKey);
        if (eventKeyResult.IsFailure)
            return Result.Failure<EventTemplateMappingResponse>(eventKeyResult.Error);

        var templateKeyResult = TemplateKey.Create(command.TemplateKey);
        if (templateKeyResult.IsFailure)
            return Result.Failure<EventTemplateMappingResponse>(templateKeyResult.Error);

        Locale? locale = null;
        if (!string.IsNullOrWhiteSpace(command.Locale))
        {
            var localeResult = Locale.Create(command.Locale);
            if (localeResult.IsFailure)
                return Result.Failure<EventTemplateMappingResponse>(localeResult.Error);
            locale = localeResult.Value;
        }

        var mappingResult = EventTemplateMapping.CreateNew(
            command.Scope,
            command.Scope == TemplateScope.Tenant ? command.TenantId : null,
            eventKeyResult.Value,
            templateKeyResult.Value,
            locale,
            command.Priority,
            DateTime.UtcNow
        );
        if (mappingResult.IsFailure)
            return Result.Failure<EventTemplateMappingResponse>(mappingResult.Error);

        await repository.AddAsync(mappingResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(EventTemplateMappingMapper.ToResponse(mappingResult.Value));
    }
}
