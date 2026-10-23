using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Consumers;

public static class FormFileScanResultConsumer
{
    public static Task Handle(
        FileAvailableIntegrationEvent evt,
        IFormRequestRepository requests,
        IFormFileScanVerdictRepository verdicts,
        IClientFormsCustomerReader customers,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<FormRequest> logger,
        IClientFormsMetrics metrics,
        CancellationToken ct
    ) =>
        ApplyAsync(
            new FormFileScanSignal(
                evt.TenantId,
                evt.FileId,
                FormFileVerdictStatus.Available,
                evt.OwnerType,
                evt.OwnerId,
                evt.FolderType,
                evt.ContentType,
                evt.SizeBytes,
                evt.OriginalName,
                evt.OccurredOn
            ),
            CorrelationOf(evt.CorrelationId, evt.EventId),
            requests,
            verdicts,
            customers,
            unitOfWork,
            bus,
            correlation,
            logger,
            metrics,
            ct
        );

    public static Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        IFormRequestRepository requests,
        IFormFileScanVerdictRepository verdicts,
        IClientFormsCustomerReader customers,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<FormRequest> logger,
        IClientFormsMetrics metrics,
        CancellationToken ct
    ) =>
        ApplyAsync(
            OwnedSignal(
                evt.TenantId,
                evt.FileId,
                FormFileVerdictStatus.Infected,
                evt.OwnerType,
                evt.OwnerId,
                evt.FolderType,
                evt.OccurredOn
            ),
            CorrelationOf(evt.CorrelationId, evt.EventId),
            requests,
            verdicts,
            customers,
            unitOfWork,
            bus,
            correlation,
            logger,
            metrics,
            ct
        );

    public static Task Handle(
        FileBlockedByPolicyIntegrationEvent evt,
        IFormRequestRepository requests,
        IFormFileScanVerdictRepository verdicts,
        IClientFormsCustomerReader customers,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<FormRequest> logger,
        IClientFormsMetrics metrics,
        CancellationToken ct
    ) =>
        ApplyAsync(
            OwnedSignal(
                evt.TenantId,
                evt.FileId,
                FormFileVerdictStatus.Blocked,
                evt.OwnerType,
                evt.OwnerId,
                evt.FolderType,
                evt.OccurredOn
            ),
            CorrelationOf(evt.CorrelationId, evt.EventId),
            requests,
            verdicts,
            customers,
            unitOfWork,
            bus,
            correlation,
            logger,
            metrics,
            ct
        );

    public static Task Handle(
        FileScanFailedIntegrationEvent evt,
        IFormRequestRepository requests,
        IFormFileScanVerdictRepository verdicts,
        IClientFormsCustomerReader customers,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<FormRequest> logger,
        IClientFormsMetrics metrics,
        CancellationToken ct
    ) =>
        ApplyAsync(
            OwnedSignal(
                evt.TenantId,
                evt.FileId,
                FormFileVerdictStatus.ScanFailed,
                evt.OwnerType,
                evt.OwnerId,
                evt.FolderType,
                evt.OccurredOn
            ),
            CorrelationOf(evt.CorrelationId, evt.EventId),
            requests,
            verdicts,
            customers,
            unitOfWork,
            bus,
            correlation,
            logger,
            metrics,
            ct
        );

    public static Task Handle(
        FileDeletedIntegrationEvent evt,
        IFormRequestRepository requests,
        IFormFileScanVerdictRepository verdicts,
        IClientFormsCustomerReader customers,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<FormRequest> logger,
        IClientFormsMetrics metrics,
        CancellationToken ct
    ) =>
        ApplyAsync(
            OwnedSignal(evt.TenantId, evt.FileId, FormFileVerdictStatus.Deleted, null, null, null, evt.OccurredOn),
            CorrelationOf(evt.CorrelationId, evt.EventId),
            requests,
            verdicts,
            customers,
            unitOfWork,
            bus,
            correlation,
            logger,
            metrics,
            ct
        );

    private static async Task ApplyAsync(
        FormFileScanSignal signal,
        string correlationId,
        IFormRequestRepository requests,
        IFormFileScanVerdictRepository verdicts,
        IClientFormsCustomerReader customers,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<FormRequest> logger,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        using (correlation.Push(correlationId))
        {
            if (!IsClientFormsFile(signal))
                return;

            var owners = OwnedByTenant(await requests.ListByAttachmentFileIdAsync(signal.FileId, ct), signal, logger);
            var projected = await verdicts.GetAsync(signal.FileId, ct);
            if (signal.FolderType is null && projected is null && owners.Count == 0)
                return;
            if (projected is not null && !BelongsToTenant(projected, signal, logger))
                return;

            var nowUtc = DateTime.UtcNow;
            var projectionChanged = projected is null || ApplyToVerdict(projected, signal, nowUtc);
            projected ??= AddVerdict(signal, verdicts, nowUtc);
            var requestChanged = await ApplyToRequestsAsync(
                owners,
                signal.FileId,
                projected,
                nowUtc,
                customers,
                bus,
                correlation,
                ct
            );

            if (projectionChanged || requestChanged)
                await SaveAsync(signal, requestChanged, unitOfWork, metrics, ct);
        }
    }

    // Solo cuenta el veredicto que llegó a algún adjunto, igual que la reconciliación.
    private static async Task SaveAsync(
        FormFileScanSignal signal,
        bool appliedToAttachment,
        IUnitOfWork unitOfWork,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        await unitOfWork.SaveChangesAsync(ct);
        if (appliedToAttachment)
            metrics.RecordAttachmentVerdict(
                ClientFormsMetricValues.Of(signal.Status),
                ClientFormsMetricValues.SourceEvent
            );
    }

    // El borrado no trae carpeta: se procesa y decide lo ya proyectado. Otra carpeta no es de Client Forms.
    private static bool IsClientFormsFile(FormFileScanSignal signal) =>
        signal.FolderType is null || string.Equals(signal.FolderType, "ClientForms", StringComparison.Ordinal);

    private static FormFileScanVerdict AddVerdict(
        FormFileScanSignal signal,
        IFormFileScanVerdictRepository verdicts,
        DateTime nowUtc
    )
    {
        var verdict = CreateVerdict(signal, nowUtc);
        verdicts.Add(verdict);
        return verdict;
    }

    private static async Task<bool> ApplyToRequestsAsync(
        List<FormRequest> owners,
        Guid fileId,
        FormFileScanVerdict projected,
        DateTime nowUtc,
        IClientFormsCustomerReader customers,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var changed = false;
        foreach (var request in owners)
            changed |= await ApplyToRequestAsync(request, fileId, projected, nowUtc, customers, bus, correlation, ct);
        return changed;
    }

    private static FormFileScanVerdict CreateVerdict(FormFileScanSignal signal, DateTime nowUtc) =>
        FormFileScanVerdict.Create(
            signal.TenantId,
            signal.FileId,
            signal.Status,
            signal.OwnerType,
            signal.OwnerId,
            signal.FolderType,
            signal.ContentType,
            signal.SizeBytes,
            signal.OriginalName,
            signal.OccurredAtUtc,
            nowUtc
        );

    // Lo que el evento no trae conserva lo ya proyectado.
    private static bool ApplyToVerdict(FormFileScanVerdict projected, FormFileScanSignal signal, DateTime nowUtc) =>
        projected.Apply(
            signal.Status,
            signal.OwnerType ?? projected.OwnerType,
            signal.OwnerId ?? projected.OwnerId,
            signal.FolderType ?? projected.FolderType,
            signal.ContentType ?? projected.ContentType,
            signal.SizeBytes ?? projected.SizeBytes,
            signal.OriginalName ?? projected.OriginalName,
            signal.OccurredAtUtc,
            nowUtc
        );

    private static bool BelongsToTenant(
        FormFileScanVerdict projected,
        FormFileScanSignal signal,
        ILogger<FormRequest> logger
    )
    {
        if (projected.TenantId == signal.TenantId)
            return true;
        logger.LogWarning(
            "File {FileId} scan event carries tenant {EventTenantId} but its form verdict belongs to {VerdictTenantId}; ignored.",
            signal.FileId,
            signal.TenantId,
            projected.TenantId
        );
        return false;
    }

    // Solo se avisa del adjunto que pasa a rechazado con este veredicto, no del que ya lo estaba.
    private static async Task<bool> ApplyToRequestAsync(
        FormRequest request,
        Guid fileId,
        FormFileScanVerdict projected,
        DateTime nowUtc,
        IClientFormsCustomerReader customers,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var before = request
            .Submission!.Attachments.Where(item => item.FileId == fileId)
            .ToDictionary(item => item.Id, item => item.Status);
        var outcome = request.ApplyFileVerdict(fileId, projected.ToVerdict(), nowUtc);
        if (outcome == FileVerdictOutcome.Ignored)
            return false;

        foreach (var attachment in request.Submission.Attachments.Where(item => item.FileId == fileId))
        {
            if (
                attachment.Status != FormAttachmentStatus.Rejected
                || before.GetValueOrDefault(attachment.Id) == FormAttachmentStatus.Rejected
            )
                continue;
            await PublishRejectedAsync(request, attachment, customers, bus, correlation.CorrelationId, ct);
        }
        return true;
    }

    // Una request de otro tenant con el mismo FileId se descarta sola; las demás reciben el veredicto.
    private static List<FormRequest> OwnedByTenant(
        IReadOnlyList<FormRequest> candidates,
        FormFileScanSignal signal,
        ILogger<FormRequest> logger
    )
    {
        var owned = new List<FormRequest>(candidates.Count);
        foreach (var request in candidates)
        {
            if (request.TenantId == signal.TenantId)
            {
                owned.Add(request);
                continue;
            }
            logger.LogWarning(
                "File {FileId} scan event carries tenant {EventTenantId} but form request {FormRequestId} belongs to {RequestTenantId}; skipped.",
                signal.FileId,
                signal.TenantId,
                request.Id,
                request.TenantId
            );
        }
        return owned;
    }

    private static async Task PublishRejectedAsync(
        FormRequest request,
        FormAttachment attachment,
        IClientFormsCustomerReader customers,
        IMessageBus bus,
        string correlationId,
        CancellationToken ct
    )
    {
        var evt = await FormRequestIntegrationEventFactory.AttachmentRejectedAsync(
            request,
            attachment,
            customers,
            correlationId,
            ct
        );
        await bus.PublishAsync(evt);
    }

    private static FormFileScanSignal OwnedSignal(
        Guid tenantId,
        Guid fileId,
        FormFileVerdictStatus status,
        string? ownerType,
        Guid? ownerId,
        string? folderType,
        DateTime occurredAtUtc
    ) => new(tenantId, fileId, status, ownerType, ownerId, folderType, null, null, null, occurredAtUtc);

    private static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
