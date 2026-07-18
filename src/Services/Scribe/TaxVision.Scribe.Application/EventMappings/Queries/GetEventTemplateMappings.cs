using BuildingBlocks.Results;

namespace TaxVision.Scribe.Application.EventMappings.Queries;

public sealed record GetEventTemplateMappingsQuery(Guid? TenantId);

public static class GetEventTemplateMappingsHandler
{
    public static async Task<Result<IReadOnlyList<EventTemplateMappingResponse>>> Handle(
        GetEventTemplateMappingsQuery query,
        IEventTemplateMappingRepository repository,
        CancellationToken ct
    )
    {
        var items = await repository.ListAsync(query.TenantId, ct);
        IReadOnlyList<EventTemplateMappingResponse> responses = items
            .Select(EventTemplateMappingMapper.ToResponse)
            .ToList();
        return Result.Success(responses);
    }
}
