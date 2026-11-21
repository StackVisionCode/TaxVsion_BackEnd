using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Requests.Files;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

/// <summary>
/// La regla del drawer del request, por archivo: adjunto disponible o PDF de una revisión de un request que pasa
/// <c>VisibleInInbox</c> (la misma regla que el detalle).
/// </summary>
public sealed class FormFileVisibilityReader(TasksDbContext context) : IFormFileVisibilityReader
{
    public async Task<IReadOnlyList<VisibleFormFile>> VisibleFormFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        Guid actorUserId,
        bool canViewAll,
        CancellationToken ct
    )
    {
        if (fileIds.Count == 0)
            return [];

        var visible = context
            .FormRequests.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .VisibleInInbox(context, tenantId, actorUserId, canViewAll);
        var attachments = await AttachmentsAsync(tenantId, fileIds, visible, ct);
        var summaries = await SummariesAsync(fileIds, visible, ct);

        return
        [
            .. attachments
                .Concat(summaries)
                .GroupBy(row => row.File.FileId)
                .Select(g => g.OrderByDescending(row => row.AtUtc).ThenBy(row => row.File.RequestId).First().File),
        ];
    }

    // El adjunto se ve con la revisión vigente del request, como al abrir el detalle.
    private async Task<List<Row>> AttachmentsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        IQueryable<FormRequest> visible,
        CancellationToken ct
    ) =>
        await context
            .FormAttachments.IgnoreQueryFilters()
            .Where(a =>
                a.TenantId == tenantId && fileIds.Contains(a.FileId) && a.Status == FormAttachmentStatus.Available
            )
            .Join(context.FormSubmissions, a => a.FormSubmissionId, s => s.Id, (a, s) => new { a, s.FormRequestId })
            .Join(
                visible,
                x => x.FormRequestId,
                r => r.Id,
                (x, r) =>
                    new Row(
                        new VisibleFormFile(x.a.FileId, r.Id, r.Title, r.CustomerId, r.CurrentRevisionNumber),
                        x.a.RegisteredAtUtc
                    )
            )
            .ToListAsync(ct);

    // La revisión no lleva tenant: lo pone el request visible del join.
    private async Task<List<Row>> SummariesAsync(
        IReadOnlyCollection<Guid> fileIds,
        IQueryable<FormRequest> visible,
        CancellationToken ct
    ) =>
        await context
            .FormSubmissionRevisions.Where(v => v.PdfFileId != null && fileIds.Contains(v.PdfFileId.Value))
            .Join(context.FormSubmissions, v => v.FormSubmissionId, s => s.Id, (v, s) => new { v, s.FormRequestId })
            .Join(
                visible,
                x => x.FormRequestId,
                r => r.Id,
                (x, r) =>
                    new Row(
                        new VisibleFormFile(x.v.PdfFileId!.Value, r.Id, r.Title, r.CustomerId, x.v.RevisionNumber),
                        x.v.SubmittedAtUtc
                    )
            )
            .ToListAsync(ct);

    private sealed record Row(VisibleFormFile File, DateTime AtUtc);
}
