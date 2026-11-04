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
/// <c>SignatureRequest</c>s en Draft esperando por este archivo, lo revisa en el servidor y
/// lo marca verificado o rechazado. El Status sigue en Draft hasta que el preparador envíe.
/// </summary>
public static class FileAvailableConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        IFileMetadataRefRepository projectionRepo,
        ISignatureRequestRepository requestRepo,
        IRequestDocumentVerifier verifier,
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
            var withHash = await VerifyWaitingDrafts(evt, requestRepo, verifier, logger, ct);
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

    private static async Task<List<SignatureRequest>> VerifyWaitingDrafts(
        FileAvailableIntegrationEvent evt,
        ISignatureRequestRepository repo,
        IRequestDocumentVerifier verifier,
        ILogger logger,
        CancellationToken ct
    )
    {
        var drafts = await repo.ListDraftsWaitingForFileAsync(evt.TenantId, evt.FileId, ct);
        if (drafts.Count == 0)
            return new List<SignatureRequest>(0);

        var file = new VerifiableFile(evt.ChecksumSha256, evt.ContentType, evt.SizeBytes);
        var ready = new List<SignatureRequest>(drafts.Count);
        foreach (var draft in drafts)
        {
            var document = draft.Documents.SingleOrDefault(candidate => candidate.OriginalFileId == evt.FileId);
            if (document is null)
                continue;

            var verification = await verifier.VerifyAsync(draft, document.Id, file, ct);
            logger.LogInformation(
                "SignatureRequest {RequestId} document {DocumentId} verification by FileAvailable {FileId}: {Outcome}.",
                draft.Id,
                document.Id,
                evt.FileId,
                verification.Kind
            );
            if (
                verification.Kind == DocumentVerificationKind.Verified
                && draft.Documents.All(item => item.DocumentHashPre is not null)
            )
                ready.Add(draft);
        }

        return ready;
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
                Documents = request
                    .Documents.Where(document => document.DocumentHashPre is not null)
                    .Select(document => new DocumentHashDescriptor(
                        document.Id,
                        document.OriginalFileId,
                        document.DocumentHashPre!.Value
                    ))
                    .ToList(),
            };
            tasks.Add(messageBus.PublishAsync(evt).AsTask());
        }

        return Task.WhenAll(tasks);
    }
}
