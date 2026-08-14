using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Abstractions;

namespace TaxVision.Tasks.Application.Templates.Queries;

public sealed record GetTaskTemplateByIdQuery(Guid TenantId, Guid TemplateId);

public static class GetTaskTemplateByIdHandler
{
    public static async Task<Result<TaskTemplateResponse>> Handle(
        GetTaskTemplateByIdQuery query,
        ITaskTemplateRepository templates,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(query.TenantId, query.TemplateId, ct);

        return found.IsFailure
            ? Result.Failure<TaskTemplateResponse>(found.Error)
            : Result.Success(TaskTemplateResponse.From(found.Value));
    }
}
