using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Work;

namespace TaxVision.Tasks.Application.Files;

/// <param name="CanViewAll">El actor tiene <c>customers.view_all</c>.</param>
public sealed record GetVisibleTaskFilesQuery(
    Guid TenantId,
    Guid ActorUserId,
    bool CanViewAll,
    IReadOnlyList<Guid> FileIds
);

/// <summary>Archivos de tareas que quien mira ve en el drawer de la tarea; lo demás se omite.</summary>
public static class GetVisibleTaskFilesHandler
{
    public static async Task<IReadOnlyList<VisibleFile>> Handle(
        GetVisibleTaskFilesQuery query,
        ITaskFileVisibilityReader reader,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (query.FileIds.Count == 0)
            return [];

        var visibleTo = WorkAccess.VisibleTo(query.ActorUserId, query.CanViewAll, visibility.Value.Enabled);
        var files = await reader.VisibleTaskFilesAsync(query.TenantId, query.FileIds, visibleTo, ct);
        return [.. files.Select(file => file with { Title = WorkLimits.ClipTitle(file.Title) })];
    }
}
