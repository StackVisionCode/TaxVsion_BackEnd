using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Attachments.Queries;

/// <param name="IncludeDescendants">
/// El padre no hereda los adjuntos del hijo: los muestra si se piden. Guardar copias arriba sería
/// duplicar la verdad.
/// </param>
public sealed record ListTaskAttachmentsQuery(Guid TenantId, Guid TaskId, bool IncludeDescendants);

public static class ListTaskAttachmentsHandler
{
    public static async Task<Result<IReadOnlyList<TaskAttachmentResponse>>> Handle(
        ListTaskAttachmentsQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var found = await tasks.GetByIdWithAttachmentsAsync(query.TenantId, query.TaskId, ct);
        if (found.IsFailure)
            return Result.Failure<IReadOnlyList<TaskAttachmentResponse>>(found.Error);

        var all = new List<TaskAttachment>(found.Value.Attachments.Where(a => a.IsActive));

        if (query.IncludeDescendants)
            all.AddRange(await DescendantAttachmentsAsync(query, tasks, ct));

        return Result.Success<IReadOnlyList<TaskAttachmentResponse>>([
            .. all.OrderBy(a => a.AttachedAtUtc).Select(TaskAttachmentResponse.From),
        ]);
    }

    private static async Task<IEnumerable<TaskAttachment>> DescendantAttachmentsAsync(
        ListTaskAttachmentsQuery query,
        ITaskRepository tasks,
        CancellationToken ct
    )
    {
        var subtasks = await tasks.ListSubtasksAsync(query.TenantId, query.TaskId, 1, 200, ct);
        var ids = subtasks.Items.Select(t => t.Id).ToList();

        if (ids.Count == 0)
            return [];

        var loaded = await tasks.ListWithAttachmentsAsync(query.TenantId, ids, ct);

        return loaded.SelectMany(t => t.Attachments).Where(a => a.IsActive);
    }
}
