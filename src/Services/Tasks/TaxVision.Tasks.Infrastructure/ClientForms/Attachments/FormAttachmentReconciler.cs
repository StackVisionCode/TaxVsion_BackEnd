using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Attachments.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Attachments;

// Los rechazos salen por el outbox de EF: se guardan en la misma transacción y solo se envían si confirma.
internal sealed class FormAttachmentReconciler(
    IDbContextOutbox<TasksDbContext> outbox,
    ITaskFileScanStatusClient files,
    IClientFormsCustomerReader customers,
    ICorrelationContext correlation,
    ITaskMetrics metrics,
    ILogger<FormAttachmentReconciler> logger
)
{
    public async Task<int> ReconcileAsync(
        TimeSpan reconcileAfter,
        TimeSpan pendingExpiry,
        int batchSize,
        CancellationToken ct = default
    )
    {
        var nowUtc = DateTime.UtcNow;
        var reconcileCutoff = nowUtc - reconcileAfter;
        var expiryCutoff = nowUtc - pendingExpiry;
        var requests = await LoadCandidatesAsync(reconcileCutoff, batchSize, ct);
        if (requests.Count == 0)
            return 0;
        var known = await LoadKnownVerdictsAsync(requests, reconcileCutoff, ct);

        var reconciled = 0;
        var expired = 0;
        using (correlation.Push($"client-forms-attachment-reconciliation-{Guid.NewGuid():N}"))
        {
            foreach (var request in requests)
            {
                reconciled += await ReconcilePendingAsync(request, known, reconcileCutoff, nowUtc, ct);
                expired += await ExpireStaleAsync(request, expiryCutoff, nowUtc, ct);
            }

            if (reconciled > 0 || expired > 0 || outbox.DbContext.ChangeTracker.HasChanges())
                await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }

        metrics.RecordClientFormsAttachmentsReconciled(reconciled, "reconciliation");
        metrics.RecordClientFormsAttachmentsReconciled(expired, "expiry");
        return reconciled + expired;
    }

    private Task<List<FormRequest>> LoadCandidatesAsync(
        DateTime reconcileCutoff,
        int batchSize,
        CancellationToken ct
    ) =>
        outbox
            .DbContext.FormRequests.IgnoreQueryFilters()
            .AsSplitQuery()
            .Include(item => item.Submission)
                .ThenInclude(submission => submission!.Attachments)
            .Where(item =>
                item.Submission != null
                && item.Submission.Attachments.Any(attachment =>
                    attachment.Status == FormAttachmentStatus.Pending && attachment.RegisteredAtUtc < reconcileCutoff
                )
            )
            .OrderBy(item => item.LastActivityAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    private Task<Dictionary<Guid, FormFileScanVerdict>> LoadKnownVerdictsAsync(
        List<FormRequest> requests,
        DateTime reconcileCutoff,
        CancellationToken ct
    )
    {
        var fileIds = requests
            .SelectMany(item => item.Submission!.Attachments)
            .Where(item => item.Status == FormAttachmentStatus.Pending && item.RegisteredAtUtc < reconcileCutoff)
            .Select(item => item.FileId)
            .Distinct()
            .ToArray();
        return outbox
            .DbContext.FormFileScanVerdicts.IgnoreQueryFilters()
            .Where(item => fileIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);
    }

    private async Task<int> ReconcilePendingAsync(
        FormRequest request,
        Dictionary<Guid, FormFileScanVerdict> known,
        DateTime reconcileCutoff,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var pending = request
            .Submission!.Attachments.Where(item =>
                item.Status == FormAttachmentStatus.Pending && item.RegisteredAtUtc < reconcileCutoff
            )
            .ToList();
        var reconciled = 0;
        foreach (var attachment in pending)
        {
            var verdict = await ResolveVerdictAsync(request, attachment, known, nowUtc, ct);
            if (verdict is null || !SharesTenant(request, attachment, verdict))
                continue;

            var before = attachment.Status;
            var outcome = request.ApplyFileVerdict(attachment.FileId, verdict.ToVerdict(), nowUtc);
            if (outcome == FileVerdictOutcome.Ignored)
                continue;
            reconciled++;
            if (attachment.Status == FormAttachmentStatus.Rejected && before != FormAttachmentStatus.Rejected)
                await PublishRejectedAsync(request, attachment, ct);
        }
        return reconciled;
    }

    // Sin veredicto local se pregunta a CloudStorage; un estado desconocido queda para la próxima vuelta.
    private async Task<FormFileScanVerdict?> ResolveVerdictAsync(
        FormRequest request,
        FormAttachment attachment,
        Dictionary<Guid, FormFileScanVerdict> known,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (known.TryGetValue(attachment.FileId, out var verdict))
            return verdict;
        var metadata = await files.GetFileMetadataAsync(request.TenantId, attachment.FileId, ct);
        if (metadata.Status == RemoteFileScanStatus.Unknown)
            return null;
        verdict = CreateVerdict(request.TenantId, attachment.FileId, metadata, nowUtc);
        outbox.DbContext.FormFileScanVerdicts.Add(verdict);
        known[attachment.FileId] = verdict;
        return verdict;
    }

    private bool SharesTenant(FormRequest request, FormAttachment attachment, FormFileScanVerdict verdict)
    {
        if (verdict.TenantId == request.TenantId)
            return true;
        logger.LogWarning(
            "File {FileId} verdict belongs to tenant {VerdictTenantId} but form request {FormRequestId} belongs to {RequestTenantId}; ignored.",
            attachment.FileId,
            verdict.TenantId,
            request.Id,
            request.TenantId
        );
        return false;
    }

    private async Task<int> ExpireStaleAsync(
        FormRequest request,
        DateTime expiryCutoff,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var expiring = request
            .Submission!.Attachments.Where(item =>
                item.Status == FormAttachmentStatus.Pending && item.RegisteredAtUtc < expiryCutoff
            )
            .ToList();
        var expired = request.ExpireStalePendingAttachments(expiryCutoff, nowUtc);
        foreach (var attachment in expiring)
            await PublishRejectedAsync(request, attachment, ct);
        return expired;
    }

    private async Task PublishRejectedAsync(FormRequest request, FormAttachment attachment, CancellationToken ct)
    {
        var evt = await FormRequestIntegrationEventFactory.AttachmentRejectedAsync(
            request,
            attachment,
            customers,
            correlation.CorrelationId,
            ct
        );
        await outbox.PublishAsync(evt);
    }

    private static FormFileScanVerdict CreateVerdict(
        Guid tenantId,
        Guid fileId,
        RemoteFileMetadata metadata,
        DateTime nowUtc
    ) =>
        FormFileScanVerdict.Create(
            tenantId,
            fileId,
            metadata.Status switch
            {
                RemoteFileScanStatus.Available => FormFileVerdictStatus.Available,
                RemoteFileScanStatus.Infected => FormFileVerdictStatus.Infected,
                RemoteFileScanStatus.BlockedByPolicy => FormFileVerdictStatus.Blocked,
                RemoteFileScanStatus.Deleted => FormFileVerdictStatus.Deleted,
                _ => FormFileVerdictStatus.ScanFailed,
            },
            metadata.OwnerType,
            metadata.OwnerId,
            metadata.FolderType,
            metadata.ContentType,
            metadata.SizeBytes,
            metadata.OriginalName,
            nowUtc,
            nowUtc
        );
}
