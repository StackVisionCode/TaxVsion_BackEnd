using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Application.Notes;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Notes.Infrastructure.Notes;

/// <summary>
/// Re-declara los adjuntos que Notes ya tenía. No modifica filas: lee por páginas y cada página publica y confirma en
/// su propia transacción con outbox. Las notas borradas y los adjuntos retirados o rechazados no se declaran.
/// </summary>
public sealed class NoteFileSourceRedeclarer(IDbContextOutbox<NotesDbContext> outbox, ICorrelationContext correlation)
    : INoteFileSourceRedeclarer
{
    public int PageSize { get; init; } = 500;

    private NotesDbContext Db => outbox.DbContext;

    public async Task<RedeclareFileSourcesReport> RedeclareAsync(Guid tenantId, bool dryRun, CancellationToken ct)
    {
        var notes = new HashSet<Guid>();
        var files = 0;
        using (correlation.Push($"notes-file-sources-redeclare-{Guid.NewGuid():N}"))
        {
            for (var page = 0; ; page++)
            {
                var declarations = await DeclarationsAsync(tenantId, null, page, correlation.CorrelationId, ct);
                foreach (var declaration in declarations)
                {
                    notes.Add(declaration.SourceResourceId);
                    files++;
                    if (!dryRun)
                        await outbox.PublishAsync(declaration);
                }
                if (!dryRun && declarations.Count > 0)
                    await outbox.SaveChangesAndFlushMessagesAsync(ct);
                if (declarations.Count < PageSize)
                    break;
            }
        }
        return new RedeclareFileSourcesReport(dryRun, notes.Count, files);
    }

    public async Task<IReadOnlyList<FileSourceDeclaredIntegrationEvent>> DeclarationsForAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        string correlationId,
        CancellationToken ct
    ) => await DeclarationsAsync(tenantId, [.. fileIds.Distinct()], null, correlationId, ct);

    // Pendientes, en revisión y disponibles: el adjunto sigue en la nota. Con fileIds, solo esos y sin páginas.
    private async Task<List<FileSourceDeclaredIntegrationEvent>> DeclarationsAsync(
        Guid tenantId,
        Guid[]? fileIds,
        int? page,
        string correlationId,
        CancellationToken ct
    )
    {
        var query = Db
            .Notes.IgnoreQueryFilters()
            .Where(n => n.TenantId == tenantId && n.Status != NoteStatus.Deleted)
            .SelectMany(
                n =>
                    n.Attachments.Where(a =>
                        a.Status != NoteAttachmentStatus.Detached && a.Status != NoteAttachmentStatus.Rejected
                    ),
                (n, a) =>
                    new
                    {
                        a.Id,
                        a.CloudStorageFileId,
                        NoteId = n.Id,
                        n.Reference.TargetType,
                        n.Reference.TargetId,
                    }
            );
        if (fileIds is not null)
            query = query.Where(r => fileIds.Contains(r.CloudStorageFileId));
        var ordered = query.OrderBy(r => r.Id);
        var rows = await (page is { } p ? ordered.Skip(p * PageSize).Take(PageSize) : ordered).ToListAsync(ct);
        return
        [
            .. rows.Select(r =>
                NoteFileSources.Declare(
                    tenantId,
                    r.NoteId,
                    r.TargetType,
                    r.TargetId,
                    r.CloudStorageFileId,
                    correlationId
                )
            ),
        ];
    }
}
