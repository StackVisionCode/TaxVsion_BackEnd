using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Templates.Abstractions;

namespace TaxVision.Tasks.Application.Templates.Queries;

/// <param name="OnlyActive">
/// El listado con el que el preparador elige qué aplicar pide sólo las activas; el de administración
/// las quiere todas, incluidas las retiradas que aún tienen encargos vivos.
/// </param>
public sealed record ListTaskTemplatesQuery(Guid TenantId, bool OnlyActive);

public static class ListTaskTemplatesHandler
{
    public static async Task<Result<IReadOnlyList<TaskTemplateResponse>>> Handle(
        ListTaskTemplatesQuery query,
        ITaskTemplateRepository templates,
        CancellationToken ct
    )
    {
        var found = await templates.ListAsync(query.TenantId, query.OnlyActive, ct);

        return Result.Success<IReadOnlyList<TaskTemplateResponse>>([.. found.Select(TaskTemplateResponse.From)]);
    }
}
