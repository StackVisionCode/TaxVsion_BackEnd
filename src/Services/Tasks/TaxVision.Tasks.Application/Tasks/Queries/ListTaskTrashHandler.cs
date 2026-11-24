using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Tasks.Queries;

public sealed record ListTaskTrashQuery(Guid TenantId, Guid ActorUserId, bool CanViewAll, int Page, int Size);

/// <summary>La papelera de la oficina, con la misma visibilidad que las tareas.</summary>
public static class ListTaskTrashHandler
{
    public const int MaxSize = 50;

    public static Task<TaskTrashPage> Handle(
        ListTaskTrashQuery query,
        ITaskTrashReader trash,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        var visibleTo = visibility.Value.Enabled && !query.CanViewAll ? query.ActorUserId : (Guid?)null;
        return trash.ListAsync(
            query.TenantId,
            visibleTo,
            Math.Max(1, query.Page),
            Math.Clamp(query.Size, 1, MaxSize),
            ct
        );
    }
}
