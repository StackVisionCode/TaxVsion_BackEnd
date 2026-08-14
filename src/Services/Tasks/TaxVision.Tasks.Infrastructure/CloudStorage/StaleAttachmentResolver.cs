using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.CloudStorage;

/// <summary>
/// Un adjunto adjuntado después de que CloudStorage ya publicó su veredicto no vuelve a recibirlo:
/// el evento sale una vez. Este barrido pregunta por los que llevan demasiado esperando y les aplica
/// el estado real, por los mismos métodos del aggregate que usa el consumer.
/// </summary>
internal sealed class StaleAttachmentResolver(
    TasksDbContext context,
    ITaskFileScanStatusClient files,
    IUnitOfWork unitOfWork,
    ILogger<StaleAttachmentResolver> logger
) : IStaleAttachmentResolver
{
    public async Task<int> ResolveAsync(TimeSpan olderThan, int batchSize, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - olderThan;

        var stale = await context
            .Tasks.IgnoreQueryFilters()
            .Include(t => t.Attachments)
            .Where(t => t.Attachments.Any(a => a.Status == AttachmentStatus.Pending && a.AttachedAtUtc < cutoff))
            .Take(batchSize)
            .ToListAsync(ct);

        var resolved = 0;

        foreach (var task in stale)
        foreach (var attachment in Pending(task, cutoff))
            resolved += await ApplyRemoteStatusAsync(task, attachment, ct) ? 1 : 0;

        if (resolved > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Resolved {Count} attachment(s) whose scan verdict had already been published.",
                resolved
            );
        }

        return resolved;
    }

    private static IEnumerable<TaskAttachment> Pending(TaskItem task, DateTime cutoff) =>
        task.Attachments.Where(a => a.Status == AttachmentStatus.Pending && a.AttachedAtUtc < cutoff).ToList();

    private async Task<bool> ApplyRemoteStatusAsync(TaskItem task, TaskAttachment attachment, CancellationToken ct)
    {
        var status = await files.GetStatusAsync(task.TenantId, attachment.FileId, ct);
        var now = DateTime.UtcNow;

        return status switch
        {
            RemoteFileScanStatus.Available => task.MarkAttachmentAvailable(attachment.FileId),
            RemoteFileScanStatus.Infected => task.MarkAttachmentRejected(attachment.FileId, "infected", now),
            RemoteFileScanStatus.BlockedByPolicy => task.MarkAttachmentRejected(
                attachment.FileId,
                "blocked-by-policy",
                now
            ),
            RemoteFileScanStatus.Deleted => task.MarkAttachmentDetached(attachment.FileId, now),

            // Sigue escaneando, o CloudStorage no respondió: se reintenta en el barrido siguiente.
            _ => false,
        };
    }
}
