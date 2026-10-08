using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Delivery;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Wolverine;

namespace TaxVision.Signature.Application.Delivery;

/// <summary>
/// Renders one partial PDF per document signed by the recipient and publishes one delivery event.
/// </summary>
public static class SignerPartialCopyConsumer
{
    private static readonly TimeSpan InProcessScanPollBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InProcessScanPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxScanWait = TimeSpan.FromMinutes(2);

    [Wolverine.Attributes.MessageTimeout(90)]
    public static async Task Handle(
        SignerPartialCopyRequestedIntegrationEvent evt,
        ISignatureRequestRepository repository,
        ISignatureCloudStorageClient storage,
        IFileMetadataRefRepository fileRefRepository,
        IPartialCopyRenderer renderer,
        IAuditChainAppender audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger<SignerPartialCopyRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(evt)))
        {
            var request = await repository.GetByIdAsync(evt.TenantId, evt.SignatureRequestId, ct);
            if (request is null)
            {
                logger.LogWarning("Partial copy skipped: request {RequestId} not found.", evt.SignatureRequestId);
                return;
            }

            var signer = request.Signers.FirstOrDefault(candidate => candidate.Id == evt.SignerId);
            if (signer is null)
            {
                logger.LogWarning("Partial copy skipped: signer {SignerId} not in request.", evt.SignerId);
                return;
            }

            if (
                evt.DocumentIds.All(documentId =>
                    signer.DocumentCompletions.Any(completion =>
                        completion.DocumentId == documentId && completion.PartialCopySentAtUtc is not null
                    )
                )
            )
            {
                logger.LogInformation(
                    "Partial copy for signer {SignerId} already delivered at {SentAt}; skipping.",
                    signer.Id,
                    signer.DocumentCompletions.Max(completion => completion.PartialCopySentAtUtc)
                );
                return;
            }

            if (signer.SignatureImageFileId is { } imageFileId)
            {
                var projection = await WaitForScanOutcomeAsync(request.TenantId, imageFileId, fileRefRepository, ct);
                if (projection?.Status != FileScanStatus.Available)
                {
                    logger.LogInformation(
                        "Partial copy for signer {SignerId} delayed: image scan {Status}.",
                        signer.Id,
                        projection?.Status
                    );
                    if (DateTime.UtcNow - evt.SignedAtUtc <= MaxScanWait)
                        throw new InvalidOperationException("Signature image not scanned yet; redeliver scheduled.");
                }
            }

            var copiesResult = await CreatePartialCopiesAsync(
                request,
                signer,
                evt,
                storage,
                fileRefRepository,
                renderer,
                logger,
                ct
            );
            if (copiesResult.IsFailure)
            {
                await RecordFailureAsync(
                    request,
                    signer.Id,
                    audit,
                    evt,
                    copiesResult.Error.Message,
                    unitOfWork,
                    logger,
                    ct
                );
                return;
            }

            var copies = copiesResult.Value;
            var now = DateTime.UtcNow;
            foreach (var copy in copies)
                request.RecordDocumentPartialCopySent(
                    signer.Id,
                    copy.File.DocumentId,
                    copy.File.PartialCopyFileId,
                    now
                );

            await audit.AppendAsync(
                request.TenantId,
                request.Id,
                SignatureAuditEventKind.PartialCopyDelivered,
                now,
                new
                {
                    signerId = signer.Id,
                    files = copies.Select(copy => new
                    {
                        copy.File.DocumentId,
                        fileId = copy.File.PartialCopyFileId,
                        checksum = copy.ChecksumSha256,
                        copy.IdempotencyKey,
                    }),
                    channel = signer.PreferredChannelLabel(),
                },
                ct
            );

            await unitOfWork.SaveChangesAsync(ct);
            await bus.PublishAsync(
                new SignerPartialCopyReadyIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    SignerId = signer.Id,
                    Files = copies.Select(copy => copy.File).ToList(),
                    SignerEmail = signer.Email.Value,
                    SignerFullName = signer.FullName.Value,
                    PhoneE164 = signer.PhoneNumber?.Value,
                    Language = signer.Language,
                    TotalSignersCount = request.Signers.Count,
                    SendSealedToSigners = request.SendSealedDocumentToSigners,
                }
            );
        }
    }

    private sealed record PartialCopyOutcome(
        PartialCopyFileDescriptor File,
        string ChecksumSha256,
        string IdempotencyKey
    );

    private static async Task<Result<IReadOnlyList<PartialCopyOutcome>>> CreatePartialCopiesAsync(
        SignatureRequest request,
        Signer signer,
        SignerPartialCopyRequestedIntegrationEvent evt,
        ISignatureCloudStorageClient storage,
        IFileMetadataRefRepository fileRefRepository,
        IPartialCopyRenderer renderer,
        ILogger logger,
        CancellationToken ct
    )
    {
        var requestedDocumentIds = evt.DocumentIds.ToHashSet();
        var documents = request
            .Documents.Where(document =>
                requestedDocumentIds.Contains(document.Id)
                && signer.DocumentCompletions.Any(completion =>
                    completion.DocumentId == document.Id && completion.PartialCopySentAtUtc is null
                )
            )
            .OrderBy(document => document.Order)
            .ToList();
        if (documents.Count == 0)
            return Result.Failure<IReadOnlyList<PartialCopyOutcome>>(
                new Error("Signature.PartialCopy.NoDocuments", "The signer has no documents to copy.")
            );

        var signedPngs = await DownloadSignedPngsAsync(request, storage, fileRefRepository, ct);
        var progress = BuildProgress(request);
        var outcomes = new List<PartialCopyOutcome>(documents.Count);
        foreach (var document in documents)
        {
            var original = await storage.DownloadAsync(request.TenantId, document.OriginalFileId, ct);
            if (original.IsFailure)
                return Result.Failure<IReadOnlyList<PartialCopyOutcome>>(original.Error);

            var fields = BuildSignedFields(request, signedPngs, signer.Id, document.Id);
            var rendered = renderer.Render(
                new PartialCopyRequest(
                    original.Value,
                    fields,
                    progress,
                    signer.FullName.Value,
                    document.Title,
                    request.SendSealedDocumentToSigners
                )
            );

            var fileName = $"{SanitizeTitle(document.Title)}_{SanitizeTitle(signer.FullName.Value)}_InProgress.pdf";
            var (ownerType, ownerId) = SealedDocumentOwner.Resolve(new[] { signer.MappedCustomerId }, request.Id);
            var upload = await storage.UploadAsync(
                request.TenantId,
                new SignatureFileUpload(
                    rendered.PdfBytes,
                    fileName,
                    "application/pdf",
                    ownerType,
                    ownerId,
                    "Signatures",
                    DateTime.UtcNow.Year,
                    request.CreatedByUserId
                ),
                ct
            );
            if (upload.IsFailure)
                return Result.Failure<IReadOnlyList<PartialCopyOutcome>>(upload.Error);

            var uploadedProjection = await WaitForScanOutcomeAsync(
                request.TenantId,
                upload.Value,
                fileRefRepository,
                ct
            );
            string? shareToken = null;
            if (uploadedProjection?.Status == FileScanStatus.Available)
            {
                var share = await storage.CreateDownloadShareLinkAsync(
                    request.TenantId,
                    upload.Value,
                    new[] { signer.Email.Value },
                    DateTime.UtcNow.AddDays(14),
                    ct
                );
                if (share.IsSuccess)
                    shareToken = share.Value;
                else
                    logger.LogWarning(
                        "Partial copy share link failed for signer {SignerId}, document {DocumentId}: {Reason}",
                        signer.Id,
                        document.Id,
                        share.Error.Message
                    );
            }
            else if (DateTime.UtcNow - evt.SignedAtUtc <= MaxScanWait)
            {
                throw new InvalidOperationException(
                    $"Partial copy file {upload.Value} not Available yet ({uploadedProjection?.Status.ToString() ?? "null"}); redeliver scheduled."
                );
            }
            else
            {
                logger.LogWarning(
                    "Partial copy file {FileId} for signer {SignerId}, document {DocumentId} is still {Status}; the email will not include its link.",
                    upload.Value,
                    signer.Id,
                    document.Id,
                    uploadedProjection?.Status
                );
            }

            var idempotencyKey = $"signature.partial_copy:{request.Id:N}:{signer.Id:N}:{document.Id:N}:v1";
            outcomes.Add(
                new PartialCopyOutcome(
                    new PartialCopyFileDescriptor(document.Id, upload.Value, document.Title, shareToken),
                    rendered.ChecksumSha256,
                    idempotencyKey
                )
            );
        }

        return Result.Success<IReadOnlyList<PartialCopyOutcome>>(outcomes);
    }

    private static string ResolveCorrelationId(SignerPartialCopyRequestedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? Guid.NewGuid().ToString("N") : evt.CorrelationId;

    private static async Task<FileMetadataRef?> WaitForScanOutcomeAsync(
        Guid tenantId,
        Guid fileId,
        IFileMetadataRefRepository fileRefRepository,
        CancellationToken ct
    )
    {
        var projection = await fileRefRepository.GetByFileIdAsync(tenantId, fileId, ct);
        if (projection is { Status: FileScanStatus.Available or FileScanStatus.Infected or FileScanStatus.Deleted })
            return projection;

        var deadline = DateTime.UtcNow + InProcessScanPollBudget;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(InProcessScanPollInterval, ct);
            projection = await fileRefRepository.GetByFileIdAsync(tenantId, fileId, ct);
            if (projection is { Status: FileScanStatus.Available or FileScanStatus.Infected or FileScanStatus.Deleted })
                return projection;
        }

        return projection;
    }

    private static async Task<IReadOnlyDictionary<Guid, byte[]>> DownloadSignedPngsAsync(
        SignatureRequest request,
        ISignatureCloudStorageClient storage,
        IFileMetadataRefRepository fileRefRepository,
        CancellationToken ct
    )
    {
        var map = new Dictionary<Guid, byte[]>();
        foreach (var signer in request.Signers)
        {
            if (signer.DocumentCompletions.Count == 0 || signer.SignatureImageFileId is not { } fileId)
                continue;

            var scan = await fileRefRepository.GetByFileIdAsync(request.TenantId, fileId, ct);
            if (scan?.Status != FileScanStatus.Available)
                continue;

            var download = await storage.DownloadAsync(request.TenantId, fileId, ct);
            if (download.IsSuccess)
                map[signer.Id] = download.Value;
        }

        return map;
    }

    private static IReadOnlyList<SealedFieldRender> BuildSignedFields(
        SignatureRequest request,
        IReadOnlyDictionary<Guid, byte[]> signedPngs,
        Guid recipientSignerId,
        Guid documentId
    )
    {
        var result = new List<SealedFieldRender>();
        foreach (
            var signer in request.Signers.Where(candidate =>
                candidate.Id == recipientSignerId
                && candidate.DocumentCompletions.Any(completion => completion.DocumentId == documentId)
            )
        )
        {
            var pngBytes = signedPngs.TryGetValue(signer.Id, out var png) ? png : null;
            foreach (var field in signer.Fields.Where(field => field.DocumentId == documentId))
            {
                var value = signer.FieldValues.FirstOrDefault(candidate => candidate.FieldId == field.Id)?.Value;
                result.Add(
                    new SealedFieldRender(
                        Page: field.Position.Page,
                        X: field.Position.X,
                        Y: field.Position.Y,
                        Width: field.Position.Width,
                        Height: field.Position.Height,
                        Kind: field.Kind,
                        Label: field.Label,
                        SignerDisplayName: signer.FullName.Value,
                        SignedAtUtc: signer.SignedAtUtc ?? DateTime.UtcNow,
                        SignatureImageBytes: field.Kind == SignatureFieldKind.Signature ? pngBytes : null,
                        Value: value
                    )
                );
            }
        }

        return result;
    }

    private static IReadOnlyList<PartialCopySignerStatus> BuildProgress(SignatureRequest request) =>
        request
            .Signers.Select(signer => new PartialCopySignerStatus(
                signer.FullName.Value,
                signer.Status switch
                {
                    SignerStatus.Signed => PartialCopySignerState.Signed,
                    SignerStatus.Rejected => PartialCopySignerState.Rejected,
                    _ => PartialCopySignerState.Pending,
                },
                signer.SignedAtUtc
            ))
            .ToList();

    private static async Task RecordFailureAsync(
        SignatureRequest request,
        Guid signerId,
        IAuditChainAppender audit,
        SignerPartialCopyRequestedIntegrationEvent evt,
        string reason,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        logger.LogError("Partial copy for {SignerId} failed: {Reason}", signerId, reason);
        foreach (var documentId in evt.DocumentIds)
            request.RecordDocumentPartialCopyFailed(signerId, documentId, reason);
        await audit.AppendAsync(
            evt.TenantId,
            evt.SignatureRequestId,
            SignatureAuditEventKind.PartialCopyFailed,
            DateTime.UtcNow,
            new
            {
                signerId,
                reason,
                idempotencyKey = evt.IdempotencyKey,
            },
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static string SanitizeTitle(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat(input.Where(character => !invalid.Contains(character))).Trim();
        return cleaned.Length > 48 ? cleaned[..48]
            : cleaned.Length == 0 ? "document"
            : cleaned;
    }
}

internal static class SignerChannelExtensions
{
    public static string PreferredChannelLabel(this Signer signer) =>
        signer.RequiredVerificationMethod?.ToString() ?? "Email";
}
