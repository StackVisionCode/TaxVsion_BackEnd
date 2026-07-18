using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Application.EventMappings.Queries;

public sealed record GetEventTemplateMappingByIdQuery(Guid Id, Guid? TenantId, bool IsPlatformAdmin);

public static class GetEventTemplateMappingByIdHandler
{
    public static async Task<Result<EventTemplateMappingResponse>> Handle(
        GetEventTemplateMappingByIdQuery query,
        IEventTemplateMappingRepository repository,
        CancellationToken ct
    )
    {
        var result = await repository.GetByIdAsync(query.Id, ct);
        if (result.IsFailure)
            return Result.Failure<EventTemplateMappingResponse>(result.Error);

        var mapping = result.Value;
        if (mapping.Scope == TemplateScope.Tenant && mapping.TenantId != query.TenantId && !query.IsPlatformAdmin)
            return Result.Failure<EventTemplateMappingResponse>(
                new Error("EventTemplateMapping.NotFound", $"Event template mapping {query.Id} was not found.")
            );

        return Result.Success(EventTemplateMappingMapper.ToResponse(mapping));
    }
}
