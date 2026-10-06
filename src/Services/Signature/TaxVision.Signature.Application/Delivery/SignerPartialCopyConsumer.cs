using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
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
/// F7 — rendea la copia parcial para un firmante y la sube a CloudStorage. El envío real por
/// email/SMS lo hace Notification al consumir <see cref="SignerPartialCopyReadyIntegrationEvent"/>.
/// Idempotente por `IdempotencyKey` (dedupe del inbox de Wolverine).
/// </summary>
public static class SignerPartialCopyConsumer
{
    // El poll bloquea el handler, así que el budget debe caber dentro del MessageTimeout de Wolverine.
    private static readonly TimeSpan InProcessScanPollBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InProcessScanPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxScanWait = TimeSpan.FromMinutes(2);

    // MessageTimeout holgado: cubre el poll + render + upload + share link sin matar el handler.
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

            var signer = request.Signers.FirstOrDefault(s => s.Id == evt.SignerId);
            if (signer is null)
            {
                logger.LogWarning("Partial copy skipped: signer {SignerId} not in request.", evt.SignerId);
                return;
            }

            // Idempotencia aguas abajo del inbox: si el aggregate ya tiene marca de entrega, no re-emito.
            if (signer.PartialCopySentAtUtc is not null)
            {
                logger.LogInformation(
                    "Partial copy for signer {SignerId} already delivered at {SentAt}; skipping.",
                    signer.Id,
                    signer.PartialCopySentAtUtc
                );
                return;
            }

            // ClamAV: la proyección FileScanStatus se alimenta por FileAvailable/FileInfected del
            // CloudStorage. Si el PNG sigue en Pending, hacemos poll corto; si tras el techo no llega,
            // seguimos con fallback tipográfico (nunca bloquear la entrega del firmante).
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
                    var elapsed = DateTime.UtcNow - evt.SignedAtUtc;
                    if (elapsed <= MaxScanWait)
                        throw new InvalidOperationException("Signature image not scanned yet; redeliver scheduled.");
                }
            }

            var originalResult = await storage.DownloadAsync(request.TenantId, request.OriginalFileId, ct);
            if (originalResult.IsFailure)
            {
                await RecordFailureAsync(
                    request,
                    signer.Id,
                    audit,
                    evt,
                    originalResult.Error.Message,
                    unitOfWork,
                    logger,
                    ct
                );
                return;
            }

            var signedPngs = await DownloadSignedPngsAsync(request, storage, fileRefRepository, ct);
            // "Email each signer a copy of what they signed" — la copia del destinatario incluye
            // SOLO sus propios campos, no las firmas de otros firmantes previos.
            var fields = BuildSignedFields(request, signedPngs, signer.Id);
            var progress = BuildProgress(request);

            var pdf = renderer.Render(
                new PartialCopyRequest(
                    originalResult.Value,
                    fields,
                    progress,
                    signer.FullName.Value,
                    request.Title,
                    request.SendSealedDocumentToSigners
                )
            );

            var fileNameBase = $"{SanitizeTitle(request.Title)}_{SanitizeTitle(signer.FullName.Value)}_InProgress.pdf";
            // Dueño en CloudStorage: misma política que el sellado — si el signer está mapeado a un
            // cliente la copia va bajo "Customer", si no "Signature". "SignatureRequest" NO existe en
            // el enum OwnerType de CloudStorage y hacía que el SaveFileRequested se descartara.
            var (ownerType, ownerId) = SealedDocumentOwner.Resolve(new[] { signer.MappedCustomerId }, request.Id);
            var upload = new SignatureFileUpload(
                Content: pdf.PdfBytes,
                FileName: fileNameBase,
                ContentType: "application/pdf",
                OwnerType: ownerType,
                OwnerId: ownerId,
                FolderType: "Signatures",
                TaxYear: DateTime.UtcNow.Year,
                ActorId: request.CreatedByUserId
            );
            var uploadResult = await storage.UploadAsync(request.TenantId, upload, ct);
            if (uploadResult.IsFailure)
            {
                await RecordFailureAsync(
                    request,
                    signer.Id,
                    audit,
                    evt,
                    uploadResult.Error.Message,
                    unitOfWork,
                    logger,
                    ct
                );
                return;
            }

            // F7 — share link solo para el firmante destinatario. UploadAsync publica el save por bus,
            // esperamos a que CloudStorage marque el archivo Available antes de pedir el link (si no,
            // "create-share-link request failed" porque el fileId aún no existe en MinIO). Hacemos el
            // wait ANTES del Save: si por timeout no llega Available, lanzamos y Wolverine reintenta
            // el mensaje completo (idempotencia por `PartialCopySentAtUtc` todavía null).
            var uploadedProjection = await WaitForScanOutcomeAsync(
                request.TenantId,
                uploadResult.Value,
                fileRefRepository,
                ct
            );
            string? shareToken = null;
            if (uploadedProjection?.Status == FileScanStatus.Available)
            {
                var shareResult = await storage.CreateDownloadShareLinkAsync(
                    request.TenantId,
                    uploadResult.Value,
                    new[] { signer.Email.Value },
                    DateTime.UtcNow.AddDays(14),
                    ct
                );
                if (shareResult.IsSuccess)
                    shareToken = shareResult.Value;
                else
                    logger.LogWarning(
                        "Partial copy share link failed for {SignerId}: {Reason}",
                        signer.Id,
                        shareResult.Error.Message
                    );
            }
            else
            {
                var elapsed = DateTime.UtcNow - evt.SignedAtUtc;
                if (elapsed <= MaxScanWait)
                    throw new InvalidOperationException(
                        $"Partial copy file {uploadResult.Value} not Available yet ({uploadedProjection?.Status.ToString() ?? "null"}); redeliver scheduled."
                    );
                logger.LogWarning(
                    "Partial copy file {FileId} for {SignerId} still {Status} after {Elapsed}; sending email without link.",
                    uploadResult.Value,
                    signer.Id,
                    uploadedProjection?.Status,
                    elapsed
                );
            }

            // Persiste el estado del aggregate SOLO después de un wait exitoso: así una excepción
            // antes de este punto deja al signer en "requested, not sent" y Wolverine reintenta.
            var now = DateTime.UtcNow;
            request.RecordPartialCopySent(signer.Id, uploadResult.Value, now);

            await audit.AppendAsync(
                request.TenantId,
                request.Id,
                SignatureAuditEventKind.PartialCopyDelivered,
                now,
                new
                {
                    signerId = signer.Id,
                    fileId = uploadResult.Value,
                    channel = signer.PreferredChannelLabel(),
                    checksum = pdf.ChecksumSha256,
                    idempotencyKey = evt.IdempotencyKey,
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
                    PartialCopyFileId = uploadResult.Value,
                    DocumentTitle = request.Title,
                    SignerEmail = signer.Email.Value,
                    SignerFullName = signer.FullName.Value,
                    PhoneE164 = signer.PhoneNumber?.Value,
                    Language = signer.Language,
                    ShareToken = shareToken,
                    TotalSignersCount = request.Signers.Count,
                    SendSealedToSigners = request.SendSealedDocumentToSigners,
                }
            );
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

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
        foreach (var s in request.Signers)
        {
            if (s.Status != Domain.Requests.SignerStatus.Signed || s.SignatureImageFileId is not { } fileId)
                continue;
            var scan = await fileRefRepository.GetByFileIdAsync(request.TenantId, fileId, ct);
            if (scan?.Status != FileScanStatus.Available)
                continue;
            var dl = await storage.DownloadAsync(request.TenantId, fileId, ct);
            if (dl.IsSuccess)
                map[s.Id] = dl.Value;
        }
        return map;
    }

    private static IReadOnlyList<SealedFieldRender> BuildSignedFields(
        SignatureRequest request,
        IReadOnlyDictionary<Guid, byte[]> signedPngs,
        Guid recipientSignerId
    )
    {
        var result = new List<SealedFieldRender>();
        foreach (
            var s in request.Signers.Where(s =>
                s.Id == recipientSignerId && s.Status == Domain.Requests.SignerStatus.Signed
            )
        )
        {
            var pngBytes = signedPngs.TryGetValue(s.Id, out var png) ? png : null;
            foreach (var f in s.Fields)
            {
                var value = s.FieldValues.FirstOrDefault(v => v.FieldId == f.Id)?.Value;
                result.Add(
                    new SealedFieldRender(
                        Page: f.Position.Page,
                        X: f.Position.X,
                        Y: f.Position.Y,
                        Width: f.Position.Width,
                        Height: f.Position.Height,
                        Kind: f.Kind,
                        Label: f.Label,
                        SignerDisplayName: s.FullName.Value,
                        SignedAtUtc: s.SignedAtUtc ?? DateTime.UtcNow,
                        SignatureImageBytes: f.Kind == SignatureFieldKind.Signature ? pngBytes : null,
                        Value: value
                    )
                );
            }
        }
        return result;
    }

    private static IReadOnlyList<PartialCopySignerStatus> BuildProgress(SignatureRequest request) =>
        request
            .Signers.Select(s => new PartialCopySignerStatus(
                s.FullName.Value,
                s.Status switch
                {
                    Domain.Requests.SignerStatus.Signed => PartialCopySignerState.Signed,
                    Domain.Requests.SignerStatus.Rejected => PartialCopySignerState.Rejected,
                    _ => PartialCopySignerState.Pending,
                },
                s.SignedAtUtc
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
        request.RecordPartialCopyFailed(signerId, reason);
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
        var cleaned = string.Concat(input.Where(c => !invalid.Contains(c))).Trim();
        return cleaned.Length > 48 ? cleaned[..48]
            : cleaned.Length == 0 ? "document"
            : cleaned;
    }
}

/// <summary>Helper local: canal preferido como etiqueta plana para el audit (no para enrutar).</summary>
internal static class SignerChannelExtensions
{
    public static string PreferredChannelLabel(this Signer signer) =>
        signer.RequiredVerificationMethod?.ToString() ?? "Email";
}
