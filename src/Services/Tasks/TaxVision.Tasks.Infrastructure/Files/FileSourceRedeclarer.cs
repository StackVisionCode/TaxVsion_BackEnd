using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.Application.Attachments;
using TaxVision.Tasks.Application.Files;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Tasks.Infrastructure.Files;

/// <summary>
/// Re-declara lo que Tasks ya tenía. No modifica filas: lee por páginas y cada página publica y confirma en su propia
/// transacción con outbox. Enlazados, de plantilla, retirados y rechazados no se declaran.
/// </summary>
public sealed class FileSourceRedeclarer(IDbContextOutbox<TasksDbContext> outbox, ICorrelationContext correlation)
    : IFileSourceRedeclarer
{
    public int PageSize { get; init; } = 500;

    private TasksDbContext Db => outbox.DbContext;

    public async Task<RedeclareFileSourcesReport> RedeclareAsync(Guid tenantId, bool dryRun, CancellationToken ct)
    {
        var tally = new Tally();
        using (correlation.Push($"tasks-file-sources-redeclare-{Guid.NewGuid():N}"))
        {
            var correlationId = correlation.CorrelationId;
            await RunAsync(page => TaskAttachmentsAsync(tenantId, null, page, correlationId, ct), dryRun, tally, ct);
            await RunAsync(page => FormAttachmentsAsync(tenantId, null, page, correlationId, ct), dryRun, tally, ct);
            await RunAsync(page => FormSummariesAsync(tenantId, null, page, correlationId, ct), dryRun, tally, ct);
        }
        return new RedeclareFileSourcesReport(dryRun, tally.Resources.Count, tally.Files);
    }

    public async Task<IReadOnlyList<FileSourceDeclaredIntegrationEvent>> DeclarationsForAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        string correlationId,
        CancellationToken ct
    )
    {
        Guid[] ids = [.. fileIds.Distinct()];
        return
        [
            .. await TaskAttachmentsAsync(tenantId, ids, null, correlationId, ct),
            .. await FormAttachmentsAsync(tenantId, ids, null, correlationId, ct),
            .. await FormSummariesAsync(tenantId, ids, null, correlationId, ct),
        ];
    }

    // Con fileIds, solo esos y sin páginas.
    private IQueryable<T> PageOf<T>(IOrderedQueryable<T> rows, int? page) =>
        page is { } p ? rows.Skip(p * PageSize).Take(PageSize) : rows;

    private async Task RunAsync(
        Func<int, Task<List<FileSourceDeclaredIntegrationEvent>>> pageOf,
        bool dryRun,
        Tally tally,
        CancellationToken ct
    )
    {
        for (var page = 0; ; page++)
        {
            var declarations = await pageOf(page);
            foreach (var declaration in declarations)
            {
                tally.Resources.Add((declaration.SourceResourceType, declaration.SourceResourceId));
                tally.Files++;
                if (!dryRun)
                    await outbox.PublishAsync(declaration);
            }
            if (!dryRun && declarations.Count > 0)
                await outbox.SaveChangesAndFlushMessagesAsync(ct);
            if (declarations.Count < PageSize)
                return;
        }
    }

    // Solo lo subido a la tarea y todavía adjunto (pendiente, en revisión o disponible).
    private async Task<List<FileSourceDeclaredIntegrationEvent>> TaskAttachmentsAsync(
        Guid tenantId,
        Guid[]? fileIds,
        int? page,
        string correlationId,
        CancellationToken ct
    )
    {
        var attachments = Db.Set<TaskAttachment>()
            .IgnoreQueryFilters()
            .Where(a =>
                a.TenantId == tenantId
                && a.Origin == AttachmentOrigin.Uploaded
                && a.Status != AttachmentStatus.Detached
                && a.Status != AttachmentStatus.Rejected
            );
        if (fileIds is not null)
            attachments = attachments.Where(a => fileIds.Contains(a.FileId));
        var query = attachments
            .Join(
                Db.Tasks.IgnoreQueryFilters().Where(t => t.TenantId == tenantId),
                a => a.TaskId,
                t => t.Id,
                (a, t) =>
                    new
                    {
                        a.Id,
                        a.FileId,
                        TaskId = t.Id,
                        t.Reference.CustomerId,
                    }
            )
            .OrderBy(r => r.Id);
        var rows = await PageOf(query, page).ToListAsync(ct);
        return
        [
            .. rows.Select(r =>
                AttachmentFileSources.Declare(tenantId, r.TaskId, r.CustomerId, r.FileId, correlationId)
            ),
        ];
    }

    private async Task<List<FileSourceDeclaredIntegrationEvent>> FormAttachmentsAsync(
        Guid tenantId,
        Guid[]? fileIds,
        int? page,
        string correlationId,
        CancellationToken ct
    )
    {
        var attachments = Db
            .FormAttachments.IgnoreQueryFilters()
            .Where(a =>
                a.TenantId == tenantId
                && a.Status != FormAttachmentStatus.Detached
                && a.Status != FormAttachmentStatus.Rejected
            );
        if (fileIds is not null)
            attachments = attachments.Where(a => fileIds.Contains(a.FileId));
        var query = attachments
            .Join(Db.FormSubmissions, a => a.FormSubmissionId, s => s.Id, (a, s) => new { a, s.FormRequestId })
            .Join(
                Db.FormRequests.IgnoreQueryFilters().Where(r => r.TenantId == tenantId),
                x => x.FormRequestId,
                r => r.Id,
                (x, r) =>
                    new
                    {
                        x.a.Id,
                        x.a.FileId,
                        RequestId = r.Id,
                        r.CustomerId,
                    }
            )
            .OrderBy(r => r.Id);
        var rows = await PageOf(query, page).ToListAsync(ct);
        return
        [
            .. rows.Select(r =>
                FormFileSources.Declare(
                    tenantId,
                    r.RequestId,
                    r.CustomerId,
                    r.FileId,
                    FormFileSources.AttachmentRole,
                    correlationId
                )
            ),
        ];
    }

    private async Task<List<FileSourceDeclaredIntegrationEvent>> FormSummariesAsync(
        Guid tenantId,
        Guid[]? fileIds,
        int? page,
        string correlationId,
        CancellationToken ct
    )
    {
        var revisions = Db.FormSubmissionRevisions.Where(v => v.PdfFileId != null);
        if (fileIds is not null)
            revisions = revisions.Where(v => fileIds.Contains(v.PdfFileId!.Value));
        var query = revisions
            .Join(Db.FormSubmissions, v => v.FormSubmissionId, s => s.Id, (v, s) => new { v, s.FormRequestId })
            .Join(
                Db.FormRequests.IgnoreQueryFilters().Where(r => r.TenantId == tenantId),
                x => x.FormRequestId,
                r => r.Id,
                (x, r) =>
                    new
                    {
                        x.v.Id,
                        FileId = x.v.PdfFileId!.Value,
                        RequestId = r.Id,
                        r.CustomerId,
                    }
            )
            .OrderBy(r => r.Id);
        var rows = await PageOf(query, page).ToListAsync(ct);
        return
        [
            .. rows.Select(r =>
                FormFileSources.Declare(
                    tenantId,
                    r.RequestId,
                    r.CustomerId,
                    r.FileId,
                    FormFileSources.SummaryRole,
                    correlationId
                )
            ),
        ];
    }

    private sealed class Tally
    {
        public HashSet<(string Type, Guid Id)> Resources { get; } = [];
        public int Files { get; set; }
    }
}
