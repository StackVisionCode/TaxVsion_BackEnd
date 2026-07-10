using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Wolverine;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Consumer del <see cref="SignatureRequestCompletedIntegrationEvent"/> que orquesta el
/// sellado end-to-end:
/// <list type="number">
///   <item>Carga el aggregate y verifica precondiciones (Completed y aún no sellado).</item>
///   <item>Descarga el PDF original de CloudStorage.</item>
///   <item>Aplica el sellado con las firmas estampadas.</item>
///   <item>Opcionalmente genera el Certificate of Completion.</item>
///   <item>Sube ambos a CloudStorage.</item>
///   <item>Registra <c>MarkSealed</c> en el aggregate.</item>
///   <item>Publica <see cref="SignatureRequestSealedIntegrationEvent"/>.</item>
/// </list>
/// Cada fase vive en un método privado con nombre autoexplicativo — no acumulan lógica.
/// Ante cualquier fallo emite <see cref="SignatureRequestSealingFailedIntegrationEvent"/>
/// y no re-lanza, para no bloquear al bus (los reintentos los gobierna Wolverine).
/// </summary>
public static class SignatureRequestCompletedConsumer
{
    private static readonly TimeSpan SealingLockTtl = TimeSpan.FromMinutes(10);

    public static async Task Handle(
        SignatureRequestCompletedIntegrationEvent evt,
        ISignatureRequestRepository repository,
        ISignatureCloudStorageClient storage,
        IDocumentSealingEngine sealer,
        ICertificateOfCompletionRenderer certificateRenderer,
        IDistributedLock distributedLock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            // Lock por request para evitar que dos réplicas del worker sellen la misma
            // solicitud en paralelo (double-processing en clúster). Si otro nodo lo tomó,
            // salimos limpiamente — el idempotency-check del pipeline evita duplicados
            // aunque el lock se salte por TTL prematuro.
            var lockKey = $"signature:sealing:{evt.SignatureRequestId:N}";
            await using var lockHandle = await distributedLock.AcquireAsync(lockKey, SealingLockTtl, ct);
            if (!lockHandle.IsAcquired)
            {
                logger.LogInformation(
                    "Sealing skipped: another node already holds the lock for request {RequestId}.",
                    evt.SignatureRequestId
                );
                return;
            }

            var request = await LoadOrSkipAsync(evt, repository, logger, ct);
            if (request is null)
                return;

            var pipeline = await SealAndPersistAsync(
                request,
                evt,
                storage,
                sealer,
                certificateRenderer,
                unitOfWork,
                logger,
                ct
            );
            if (pipeline.IsFailure)
            {
                await PublishFailedAsync(request, pipeline.Error, correlationId, bus);
                return;
            }

            await PublishSealedAsync(request, pipeline.Value, correlationId, bus);
        }
    }

    private sealed record PipelineOutcome(
        Guid SealedFileId,
        string HashPost,
        Guid? CertificateFileId,
        DateTime SealedAtUtc
    );

    // ============== Fase 1: cargar aggregate y saltar si ya está sellado ==============

    private static async Task<SignatureRequest?> LoadOrSkipAsync(
        SignatureRequestCompletedIntegrationEvent evt,
        ISignatureRequestRepository repository,
        ILogger logger,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(evt.TenantId, evt.SignatureRequestId, ct);
        if (request is null)
        {
            logger.LogWarning(
                "Sealing skipped: SignatureRequest {RequestId} not found for tenant {TenantId}.",
                evt.SignatureRequestId,
                evt.TenantId
            );
            return null;
        }

        if (request.Status != SignatureRequestStatus.Completed)
        {
            logger.LogInformation(
                "Sealing skipped: SignatureRequest {RequestId} is not Completed (status={Status}).",
                request.Id,
                request.Status
            );
            return null;
        }

        if (request.SealedFileId is not null)
        {
            logger.LogInformation(
                "Sealing skipped: SignatureRequest {RequestId} is already sealed (fileId={SealedFileId}).",
                request.Id,
                request.SealedFileId
            );
            return null;
        }

        return request;
    }

    // ============== Fase 2..6: pipeline de sellado ==============

    private static async Task<Result<PipelineOutcome>> SealAndPersistAsync(
        SignatureRequest request,
        SignatureRequestCompletedIntegrationEvent evt,
        ISignatureCloudStorageClient storage,
        IDocumentSealingEngine sealer,
        ICertificateOfCompletionRenderer certificateRenderer,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        var originalBytesResult = await storage.DownloadAsync(request.TenantId, request.OriginalFileId, ct);
        if (originalBytesResult.IsFailure)
            return Result.Failure<PipelineOutcome>(originalBytesResult.Error);

        var sealResult = ApplySeal(request, evt, originalBytesResult.Value, sealer);
        var sealedUpload = BuildSealedUpload(request, sealResult);
        var sealedFileIdResult = await storage.UploadAsync(request.TenantId, sealedUpload, ct);
        if (sealedFileIdResult.IsFailure)
            return Result.Failure<PipelineOutcome>(sealedFileIdResult.Error);

        var certificateFileId = await MaybeGenerateCertificateAsync(
            request,
            sealResult,
            certificateRenderer,
            storage,
            logger,
            ct
        );
        if (certificateFileId.IsFailure)
            return Result.Failure<PipelineOutcome>(certificateFileId.Error);

        var sealedAt = DateTime.UtcNow;
        var persistence = await PersistOnAggregateAsync(
            request,
            sealedFileIdResult.Value,
            sealResult.ChecksumSha256,
            certificateFileId.Value,
            unitOfWork,
            ct
        );
        if (persistence.IsFailure)
            return Result.Failure<PipelineOutcome>(persistence.Error);

        return Result.Success(
            new PipelineOutcome(sealedFileIdResult.Value, sealResult.ChecksumSha256, certificateFileId.Value, sealedAt)
        );
    }

    private static SealingResult ApplySeal(
        SignatureRequest request,
        SignatureRequestCompletedIntegrationEvent evt,
        byte[] originalBytes,
        IDocumentSealingEngine sealer
    )
    {
        var fields = BuildFieldRenders(request);
        var footer = $"SignatureRequest={request.Id:D} • CompletedAt={evt.CompletedAtUtc:O}";
        var sealingRequest = new SealingRequest(originalBytes, fields, evt.DocumentHashPre, footer);
        return sealer.Seal(sealingRequest);
    }

    private static IReadOnlyList<SealedFieldRender> BuildFieldRenders(SignatureRequest request)
    {
        var renders = new List<SealedFieldRender>();
        foreach (var signer in request.Signers)
        {
            var signedAt = signer.SignedAtUtc ?? DateTime.UtcNow;
            foreach (var field in signer.Fields)
            {
                renders.Add(
                    new SealedFieldRender(
                        Page: field.Position.Page,
                        X: field.Position.X,
                        Y: field.Position.Y,
                        Width: field.Position.Width,
                        Height: field.Position.Height,
                        Kind: field.Kind,
                        Label: field.Label,
                        SignerDisplayName: signer.FullName.Value,
                        SignedAtUtc: signedAt
                    )
                );
            }
        }
        return renders;
    }

    private static SignaturePdfUpload BuildSealedUpload(SignatureRequest request, SealingResult sealResult) =>
        new(
            Content: sealResult.SealedPdfBytes,
            FileName: $"signed-{request.Id:D}.pdf",
            ContentType: "application/pdf",
            OwnerType: "SignatureRequest",
            OwnerId: request.Id,
            FolderType: "SignatureSealed",
            TaxYear: null
        );

    // ============== Fase 3b: certificate opcional ==============

    private static async Task<Result<Guid?>> MaybeGenerateCertificateAsync(
        SignatureRequest request,
        SealingResult sealResult,
        ICertificateOfCompletionRenderer renderer,
        ISignatureCloudStorageClient storage,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (!request.GenerateCertificate)
            return Result.Success<Guid?>(null);

        var model = BuildCertificateModel(request, sealResult);
        var rendered = renderer.Render(model);
        var upload = new SignaturePdfUpload(
            Content: rendered.CertificatePdfBytes,
            FileName: $"certificate-{request.Id:D}.pdf",
            ContentType: "application/pdf",
            OwnerType: "SignatureRequest",
            OwnerId: request.Id,
            FolderType: "SignatureCertificate",
            TaxYear: null
        );
        var uploadResult = await storage.UploadAsync(request.TenantId, upload, ct);
        if (uploadResult.IsFailure)
        {
            logger.LogWarning(
                "Certificate upload failed for {RequestId}: {Error}",
                request.Id,
                uploadResult.Error.Message
            );
            return Result.Failure<Guid?>(uploadResult.Error);
        }

        return Result.Success<Guid?>(uploadResult.Value);
    }

    private static CertificateOfCompletionModel BuildCertificateModel(
        SignatureRequest request,
        SealingResult sealResult
    ) =>
        new(
            TenantId: request.TenantId,
            SignatureRequestId: request.Id,
            Title: request.Title,
            Category: request.Category,
            CreatedAtUtc: request.CreatedAtUtc,
            CompletedAtUtc: request.CompletedAtUtc ?? DateTime.UtcNow,
            DocumentHashPre: request.DocumentHashPre?.Value ?? string.Empty,
            DocumentHashPost: sealResult.ChecksumSha256,
            Signers: request
                .Signers.Select(s => new CertificateSignerEntry(
                    s.FullName.Value,
                    s.Email.Value,
                    s.Order,
                    s.Status,
                    s.SignedAtUtc,
                    s.ClientIp,
                    s.UserAgent
                ))
                .ToList()
        );

    // ============== Fase 5: persistir en el aggregate ==============

    private static async Task<Result> PersistOnAggregateAsync(
        SignatureRequest request,
        Guid sealedFileId,
        string hashPost,
        Guid? certificateFileId,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var hashResult = DocumentHash.Create(hashPost);
        if (hashResult.IsFailure)
            return Result.Failure(hashResult.Error);

        var markResult = request.MarkSealed(sealedFileId, hashResult.Value, certificateFileId);
        if (markResult.IsFailure)
            return markResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ============== Fase 6: publicar Sealed / Failed ==============

    private static Task PublishSealedAsync(
        SignatureRequest request,
        PipelineOutcome outcome,
        string correlationId,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestSealedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlationId,
                    SignatureRequestId = request.Id,
                    SealedFileId = outcome.SealedFileId,
                    DocumentHashPost = outcome.HashPost,
                    CertificateFileId = outcome.CertificateFileId,
                    SealedAtUtc = outcome.SealedAtUtc,
                }
            )
            .AsTask();

    private static Task PublishFailedAsync(
        SignatureRequest request,
        Error error,
        string correlationId,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestSealingFailedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlationId,
                    SignatureRequestId = request.Id,
                    Reason = error.Message,
                    ErrorCode = error.Code,
                    FailedAtUtc = DateTime.UtcNow,
                }
            )
            .AsTask();

    // ============== Helpers ==============

    private static string ResolveCorrelationId(SignatureRequestCompletedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
