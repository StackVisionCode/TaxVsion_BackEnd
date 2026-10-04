using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Wolverine;

namespace TaxVision.Signature.Application.Projections.CloudStorageEvents;

/// <summary>
/// El archivo pasó el scan y está disponible. Actualiza la proyección local y, si hay
/// <c>SignatureRequest</c>s en Draft esperando por este archivo, les adjunta el hash
/// original. El Status sigue en Draft hasta que el preparador envíe.
/// </summary>
public static class FileAvailableConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        IFileMetadataRefRepository projectionRepo,
        ISignatureRequestRepository requestRepo,
        IUnitOfWork unitOfWork,
        IMessageBus messageBus,
        ICorrelationContext correlation,
        ILogger<FileMetadataRef> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            await UpsertProjection(evt, projectionRepo, ct);
            var withHash = await AttachHashToWaitingDrafts(evt, requestRepo, logger, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await PublishReadyEvents(withHash, correlationId, messageBus);
        }
    }

    private static string ResolveCorrelationId(FileAvailableIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

    private static async Task UpsertProjection(
        FileAvailableIntegrationEvent evt,
        IFileMetadataRefRepository repo,
        CancellationToken ct
    )
    {
        var existing = await repo.GetByFileIdAsync(evt.TenantId, evt.FileId, ct);
        if (existing is null)
        {
            var projection = FileMetadataRef.ForAvailable(
                evt.TenantId,
                evt.FileId,
                evt.ObjectKey,
                evt.ContentType,
                evt.SizeBytes,
                evt.ChecksumSha256
            );
            await repo.AddAsync(projection, ct);
        }
        else
        {
            existing.MarkAvailable(evt.ObjectKey, evt.ContentType, evt.SizeBytes, evt.ChecksumSha256);
        }
    }

    private static async Task<List<SignatureRequest>> AttachHashToWaitingDrafts(
        FileAvailableIntegrationEvent evt,
        ISignatureRequestRepository repo,
        ILogger logger,
        CancellationToken ct
    )
    {
        var drafts = await repo.ListDraftsWaitingForFileAsync(evt.TenantId, evt.FileId, ct);
        if (drafts.Count == 0)
            return new List<SignatureRequest>(0);

        var hashResult = DocumentHash.Create(evt.ChecksumSha256);
        if (hashResult.IsFailure)
        {
            logger.LogWarning(
                "FileAvailable {FileId} carries an invalid checksum; hash will not be attached to drafts: {Error}",
                evt.FileId,
                hashResult.Error.Message
            );
            return new List<SignatureRequest>(0);
        }

        var attached = new List<SignatureRequest>(drafts.Count);
        foreach (var draft in drafts)
        {
            var result = draft.AttachOriginalHash(hashResult.Value);
            if (result.IsSuccess)
            {
                attached.Add(draft);
                logger.LogInformation(
                    "SignatureRequest {RequestId} hash attached by FileAvailable {FileId} (still Draft until send).",
                    draft.Id,
                    evt.FileId
                );
            }
            else
            {
                logger.LogWarning(
                    "SignatureRequest {RequestId} failed to attach original hash: {Error}",
                    draft.Id,
                    result.Error.Message
                );
            }
        }

        return attached;
    }

    private static Task PublishReadyEvents(
        IReadOnlyList<SignatureRequest> promoted,
        string correlationId,
        IMessageBus messageBus
    )
    {
        if (promoted.Count == 0)
            return Task.CompletedTask;

        var tasks = new List<Task>(promoted.Count);
        foreach (var request in promoted)
        {
            var evt = new SignatureRequestReadyForSendingIntegrationEvent
            {
                TenantId = request.TenantId,
                CorrelationId = correlationId,
                SignatureRequestId = request.Id,
                CreatedByUserId = request.CreatedByUserId,
                OriginalFileId = request.OriginalFileId,
                DocumentHashPre = request.DocumentHashPre!.Value,
            };
            tasks.Add(messageBus.PublishAsync(evt).AsTask());
        }

        return Task.WhenAll(tasks);
    }
}
