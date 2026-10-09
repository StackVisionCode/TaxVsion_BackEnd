using System.Globalization;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Projections;
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
///   <item>Publica el resultado de sellado por documento y el cierre de la solicitud.</item>
/// </list>
/// Cada fase vive en un método privado con nombre autoexplicativo — no acumulan lógica.
/// Ante cualquier fallo emite <see cref="SignatureRequestSealingFailedIntegrationEvent"/>
/// y no re-lanza, para no bloquear al bus (los reintentos los gobierna Wolverine).
/// </summary>
public static class SignatureRequestCompletedConsumer
{
    private static readonly TimeSpan SealingLockTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Tope de espera del scan de la imagen de firma antes de sellar con fallback tipográfico. Supera la
    /// suma de cooldowns del RetryWithCooldown dedicado (~2 min) para dar una última oportunidad al scan;
    /// pasado esto, la entrega NUNCA se bloquea (se degrada el sello, no se pierde el envelope).
    /// </summary>
    private static readonly TimeSpan MaxSignatureImageScanWait = TimeSpan.FromMinutes(2);

    // El 99% de las "carreras" entre SignatureRequestCompleted y FileAvailable son de ~500 ms:
    // el `/sign` publica Completed inmediatamente, el PNG ya está en MinIO y Clam escanea en
    // paralelo. Antes de rendirnos y pedirle a Wolverine un redelivery (que ensucia el log con
    // Error y dobla el handler), aguantamos aquí mismo con un poll corto: refresca la proyección
    // cada 300 ms hasta 5 s. Si en ese plazo el FileAvailable propagó, el consumer sigue sin
    // romper nada; si no, tira la excepción y el redelivery de Wolverine actúa como lo hacía.
    private static readonly TimeSpan InProcessScanPollBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InProcessScanPollInterval = TimeSpan.FromMilliseconds(300);

    public static Task Handle(
        SignatureRequestCompletedIntegrationEvent evt,
        ISignatureRequestRepository repository,
        ISignatureCloudStorageClient storage,
        IFileMetadataRefRepository fileRefRepository,
        ITenantBrandingRefRepository brandingRepository,
        IDocumentSealingEngine sealer,
        ICertificateOfCompletionRenderer certificateRenderer,
        IDistributedLock distributedLock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        CancellationToken ct
    ) =>
        HandleCore(
            evt.TenantId,
            evt.SignatureRequestId,
            evt.Documents.Select(document => document.DocumentId).ToList(),
            evt.CompletedAtUtc,
            evt.CorrelationId,
            evt.EventId,
            repository,
            storage,
            fileRefRepository,
            brandingRepository,
            sealer,
            certificateRenderer,
            distributedLock,
            unitOfWork,
            bus,
            correlation,
            logger,
            ct
        );

    public static Task Handle(
        SignatureDocumentsReadyForSealingIntegrationEvent evt,
        ISignatureRequestRepository repository,
        ISignatureCloudStorageClient storage,
        IFileMetadataRefRepository fileRefRepository,
        ITenantBrandingRefRepository brandingRepository,
        IDocumentSealingEngine sealer,
        ICertificateOfCompletionRenderer certificateRenderer,
        IDistributedLock distributedLock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        CancellationToken ct
    ) =>
        HandleCore(
            evt.TenantId,
            evt.SignatureRequestId,
            evt.DocumentIds,
            evt.ReadyAtUtc,
            evt.CorrelationId,
            evt.EventId,
            repository,
            storage,
            fileRefRepository,
            brandingRepository,
            sealer,
            certificateRenderer,
            distributedLock,
            unitOfWork,
            bus,
            correlation,
            logger,
            ct
        );

    private static async Task HandleCore(
        Guid tenantId,
        Guid signatureRequestId,
        IReadOnlyList<Guid> requestedDocumentIds,
        DateTime evidenceAtUtc,
        string? sourceCorrelationId,
        Guid sourceEventId,
        ISignatureRequestRepository repository,
        ISignatureCloudStorageClient storage,
        IFileMetadataRefRepository fileRefRepository,
        ITenantBrandingRefRepository brandingRepository,
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
        var correlationId = ResolveCorrelationId(sourceCorrelationId, sourceEventId);
        using (correlation.Push(correlationId))
        {
            // Lock por request para evitar que dos réplicas del worker sellen la misma
            // solicitud en paralelo (double-processing en clúster). Si otro nodo lo tomó,
            // salimos limpiamente — el idempotency-check del pipeline evita duplicados
            // aunque el lock se salte por TTL prematuro.
            var lockKey = $"signature:sealing:{signatureRequestId:N}";
            await using var lockHandle = await distributedLock.AcquireAsync(lockKey, SealingLockTtl, ct);
            if (!lockHandle.IsAcquired)
            {
                logger.LogInformation(
                    "Sealing skipped: another node already holds the lock for request {RequestId}.",
                    signatureRequestId
                );
                return;
            }

            var request = await LoadOrSkipAsync(
                tenantId,
                signatureRequestId,
                requestedDocumentIds,
                repository,
                logger,
                ct
            );
            if (request is null)
                return;

            // Gate anti-carrera con ClamAV: si algún PNG de firma aún no pasó el scan (proyección
            // ausente/Pending), lanza SignatureImageNotReadyException → Wolverine redelivera con
            // cooldown hasta que llegue FileAvailable. Devuelve los firmantes cuya imagen ya está
            // Available (los Infected/Deleted se excluyen: sellan con fallback tipográfico).
            var readyImageSignerIds = await ResolveScannedSignatureImagesAsync(
                request,
                requestedDocumentIds,
                evidenceAtUtc,
                fileRefRepository,
                logger,
                ct
            );

            var pipeline = await SealAndPersistAsync(
                request,
                requestedDocumentIds,
                evidenceAtUtc,
                readyImageSignerIds,
                storage,
                brandingRepository,
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

    private sealed record DocumentSealOutcome(
        Guid DocumentId,
        Guid SealedFileId,
        string HashPost,
        DateTime SealedAtUtc
    );

    private sealed record PipelineOutcome(
        IReadOnlyList<DocumentSealOutcome> SealedDocuments,
        Guid? CertificateFileId,
        IReadOnlyDictionary<Guid, Guid> DocumentCertificateFileIds,
        DateTime CompletedAtUtc,
        bool RequestSealingCompleted
    );

    private sealed record CertificateRenderArtifact(Guid? DocumentId, string Title, byte[] Bytes);

    // ============== Fase 1: cargar aggregate y saltar si ya está sellado ==============

    private static async Task<SignatureRequest?> LoadOrSkipAsync(
        Guid tenantId,
        Guid signatureRequestId,
        IReadOnlyList<Guid> requestedDocumentIds,
        ISignatureRequestRepository repository,
        ILogger logger,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(tenantId, signatureRequestId, ct);
        if (request is null)
        {
            logger.LogWarning(
                "Sealing skipped: SignatureRequest {RequestId} not found for tenant {TenantId}.",
                signatureRequestId,
                tenantId
            );
            return null;
        }

        if (request.Status is not (SignatureRequestStatus.InProgress or SignatureRequestStatus.Completed))
        {
            logger.LogInformation(
                "Sealing skipped: SignatureRequest {RequestId} cannot seal documents in status {Status}.",
                request.Id,
                request.Status
            );
            return null;
        }

        var requested = requestedDocumentIds.ToHashSet();
        var hasReadyDocument = request.Documents.Any(document =>
            requested.Contains(document.Id)
            && document.SealedFileId is null
            && request.IsDocumentReadyForSealing(document.Id)
        );
        var needsCertificate =
            request.Status == SignatureRequestStatus.Completed
            && request.AllDocumentsSealed()
            && request.GenerateCertificate
            && !request.AllCertificateArtifactsGenerated();
        if (!hasReadyDocument && !needsCertificate)
        {
            logger.LogInformation(
                "Sealing skipped: all documents for SignatureRequest {RequestId} are already sealed.",
                request.Id
            );
            return null;
        }

        return request;
    }

    // ============== Fase 2..6: pipeline de sellado ==============

    // ============== Gate anti-carrera ClamAV ==============

    /// <summary>
    /// Recorre los firmantes con imagen de firma y consulta la proyección local de readiness
    /// (<see cref="FileMetadataRef"/>, alimentada por FileAvailable/FileInfected de CloudStorage):
    /// <list type="bullet">
    ///   <item><description>Ausente o <c>Pending</c> → aún escaneando: lanza
    ///     <see cref="SignatureImageNotReadyException"/> para que Wolverine reintente el sellado.</description></item>
    ///   <item><description><c>Available</c> → el id del firmante entra al set descargable.</description></item>
    ///   <item><description><c>Infected</c>/<c>Deleted</c> → se excluye (nunca se embebe): ese campo
    ///     cae al sello tipográfico. No se reintenta porque no va a mejorar.</description></item>
    /// </list>
    /// </summary>
    /// <summary>
    /// Resuelve la proyección de scan absorbiendo la carrera corta entre `SignatureRequestCompleted`
    /// (publicado al firmar) y `FileAvailable` (publicado por el escáner de CloudStorage). Si al primer
    /// vistazo la proyección sigue en Pending/ausente, hace poll in-process cada
    /// <see cref="InProcessScanPollInterval"/> hasta agotar <see cref="InProcessScanPollBudget"/>. Al
    /// terminar devuelve el último snapshot — el llamador decide qué hacer con un Pending residual.
    /// </summary>
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

    private static async Task<IReadOnlySet<Guid>> ResolveScannedSignatureImagesAsync(
        SignatureRequest request,
        IReadOnlyList<Guid> requestedDocumentIds,
        DateTime evidenceAtUtc,
        IFileMetadataRefRepository fileRefRepository,
        ILogger logger,
        CancellationToken ct
    )
    {
        var ready = new HashSet<Guid>();
        var requested = requestedDocumentIds.ToHashSet();
        foreach (
            var signer in request.Signers.Where(signer =>
                signer.Fields.Any(field => requested.Contains(field.DocumentId))
            )
        )
        {
            if (signer.SignatureImageFileId is not { } imageFileId)
                continue;

            var projection = await WaitForScanOutcomeAsync(request.TenantId, imageFileId, fileRefRepository, ct);
            switch (projection?.Status)
            {
                case FileScanStatus.Available:
                    ready.Add(signer.Id);
                    break;

                case FileScanStatus.Infected:
                case FileScanStatus.Deleted:
                    logger.LogWarning(
                        "Signature image {FileId} for signer {SignerId} is {Status}; sealing with typographic fallback.",
                        imageFileId,
                        signer.Id,
                        projection.Status
                    );
                    break;

                default:
                    // Aún escaneando (proyección ausente/Pending). Reintentar da tiempo al scan, PERO con
                    // un tope: si tras MaxScanWait el archivo sigue sin estar Available (scan roto/atascado,
                    // o la subida nunca se registró), NO se bloquea la entrega para siempre — se sella con
                    // fallback tipográfico y se emite el documento. Mejor un sello degradado entregado que
                    // un envelope que nunca llega al firmante.
                    var elapsed = DateTime.UtcNow - evidenceAtUtc;
                    if (elapsed > MaxSignatureImageScanWait)
                    {
                        logger.LogWarning(
                            "Signature image {FileId} for signer {SignerId} is still not Available after {Elapsed}; "
                                + "sealing with typographic fallback to avoid blocking delivery.",
                            imageFileId,
                            signer.Id,
                            elapsed
                        );
                        break;
                    }

                    throw new SignatureImageNotReadyException(
                        $"Signature image {imageFileId} for signer {signer.Id} has not finished virus scanning yet."
                    );
            }
        }

        return ready;
    }

    private static async Task<Result<PipelineOutcome>> SealAndPersistAsync(
        SignatureRequest request,
        IReadOnlyList<Guid> requestedDocumentIds,
        DateTime evidenceAtUtc,
        IReadOnlySet<Guid> readyImageSignerIds,
        ISignatureCloudStorageClient storage,
        ITenantBrandingRefRepository brandingRepository,
        IDocumentSealingEngine sealer,
        ICertificateOfCompletionRenderer certificateRenderer,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        // Bajamos aquí (donde vive el I/O de CloudStorage) el PNG de firma de cada firmante cuyo archivo
        // ya pasó el scan (readyImageSignerIds), para que el engine estampe la imagen y quede puro (sin I/O).
        var signatureImages = await DownloadSignatureImagesAsync(request, readyImageSignerIds, storage, ct);

        // Firma reutilizable del preparador (canal paralelo, Form 8879). Best-effort: la imagen se subió al
        // crear el perfil (F1), así que ya está Available; si por lo que sea no baja, cae al sello tipográfico.
        var preparerImage = await DownloadPreparerSignatureAsync(request, storage, ct);

        var sealedDocuments = new List<DocumentSealOutcome>();
        var requested = requestedDocumentIds.ToHashSet();
        foreach (var document in request.Documents.OrderBy(document => document.Order))
        {
            if (
                !requested.Contains(document.Id)
                || document.SealedFileId is not null
                || !request.IsDocumentReadyForSealing(document.Id)
            )
                continue;

            var originalBytesResult = await storage.DownloadAsync(request.TenantId, document.OriginalFileId, ct);
            if (originalBytesResult.IsFailure)
                return Result.Failure<PipelineOutcome>(originalBytesResult.Error);

            var sealResult = ApplySeal(
                request,
                evidenceAtUtc,
                document,
                originalBytesResult.Value,
                signatureImages,
                preparerImage,
                sealer
            );
            var sealedUpload = BuildSealedUpload(request, document, sealResult);
            var sealedFileIdResult = await storage.UploadAsync(request.TenantId, sealedUpload, ct);
            if (sealedFileIdResult.IsFailure)
                return Result.Failure<PipelineOutcome>(sealedFileIdResult.Error);

            var hashResult = DocumentHash.Create(sealResult.ChecksumSha256);
            if (hashResult.IsFailure)
                return Result.Failure<PipelineOutcome>(hashResult.Error);

            var sealedAtUtc = DateTime.UtcNow;
            var marked = request.MarkDocumentSealed(
                document.Id,
                sealedFileIdResult.Value,
                hashResult.Value,
                sealedAtUtc
            );
            if (marked.IsFailure)
                return Result.Failure<PipelineOutcome>(marked.Error);

            sealedDocuments.Add(
                new DocumentSealOutcome(document.Id, sealedFileIdResult.Value, sealResult.ChecksumSha256, sealedAtUtc)
            );
        }

        var requestSealingCompleted =
            request.Status == SignatureRequestStatus.Completed && request.AllDocumentsSealed();

        if (!requestSealingCompleted)
        {
            if (sealedDocuments.Count > 0)
                await unitOfWork.SaveChangesAsync(ct);

            return Result.Success(
                new PipelineOutcome(
                    sealedDocuments,
                    request.CertificateFileId,
                    request
                        .Documents.Where(document => document.CertificateFileId is not null)
                        .ToDictionary(document => document.Id, document => document.CertificateFileId!.Value),
                    DateTime.UtcNow,
                    RequestSealingCompleted: false
                )
            );
        }

        // ORDEN CRÍTICO (carrera con el commit): el handler de sellado corre bajo una transacción de
        // Wolverine que commitea al FINAL, pero las subidas publican SaveFileRequested de inmediato, así
        // que CloudStorage escanea y emite FileAvailable ANTES del commit. SealedReady/CertReady resuelven
        // el request por Sealed/CertificateFileId, que aún no están persistidos si el FileAvailable gana la
        // carrera → se perdía el correo de "documento firmado". Solución: hacer TODO el trabajo lento
        // (render del certificado + descarga del logo de oficina, ~1-2s) ANTES de subir nada, y luego subir
        // sellado y certificado espalda con espalda justo antes del commit. Así ambos FileAvailable llegan
        // con una ventana mínima (~ms) respecto al commit y los consumers encuentran el request.
        var certificateArtifactsResult = await GenerateCertificateArtifactsAsync(
            request,
            certificateRenderer,
            storage,
            brandingRepository,
            logger,
            ct
        );
        if (certificateArtifactsResult.IsFailure)
            return Result.Failure<PipelineOutcome>(certificateArtifactsResult.Error);

        // Certificado PRIMERO y sellado de ÚLTIMO: así el FileAvailable del sellado (el correo que fallaba)
        // llega con la ventana más chica posible respecto al commit — sube y a renglón seguido se persiste.
        var certificateFileId = request.CertificateFileId;
        var documentCertificateFileIds = request
            .Documents.Where(document => document.CertificateFileId is not null)
            .ToDictionary(document => document.Id, document => document.CertificateFileId!.Value);
        foreach (var artifact in certificateArtifactsResult.Value)
        {
            var certificateUpload = BuildCertificateUpload(request, artifact.Title, artifact.Bytes);
            var certificateUploadResult = await storage.UploadAsync(request.TenantId, certificateUpload, ct);
            if (certificateUploadResult.IsFailure)
            {
                logger.LogWarning(
                    "Certificate upload failed for {RequestId}: {Error}",
                    request.Id,
                    certificateUploadResult.Error.Message
                );
                return Result.Failure<PipelineOutcome>(certificateUploadResult.Error);
            }

            var recorded = artifact.DocumentId is { } documentId
                ? request.RecordDocumentCertificate(documentId, certificateUploadResult.Value)
                : request.RecordCertificate(certificateUploadResult.Value);
            if (recorded.IsFailure)
                return Result.Failure<PipelineOutcome>(recorded.Error);

            if (artifact.DocumentId is { } perDocumentId)
                documentCertificateFileIds[perDocumentId] = certificateUploadResult.Value;
            else
                certificateFileId = certificateUploadResult.Value;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(
            new PipelineOutcome(
                sealedDocuments,
                certificateFileId,
                documentCertificateFileIds,
                DateTime.UtcNow,
                RequestSealingCompleted: true
            )
        );
    }

    private static async Task<IReadOnlyDictionary<Guid, byte[]>> DownloadSignatureImagesAsync(
        SignatureRequest request,
        IReadOnlySet<Guid> readyImageSignerIds,
        ISignatureCloudStorageClient storage,
        CancellationToken ct
    )
    {
        var images = new Dictionary<Guid, byte[]>();
        foreach (var signer in request.Signers)
        {
            if (signer.SignatureImageFileId is not { } imageFileId || !readyImageSignerIds.Contains(signer.Id))
                continue;

            var downloadResult = await storage.DownloadAsync(request.TenantId, imageFileId, ct);
            if (downloadResult.IsSuccess)
            {
                images[signer.Id] = downloadResult.Value;
                continue;
            }

            // El gate ya confirmó Available, así que un fallo aquí es transitorio (blip de MinIO/red),
            // no "archivo inexistente". Reintentar el sellado es preferible a sellar sin la firma real.
            throw new SignatureImageNotReadyException(
                $"Signature image {imageFileId} for signer {signer.Id} is Available but could not be downloaded ({downloadResult.Error.Code})."
            );
        }

        return images;
    }

    private static async Task<byte[]?> DownloadPreparerSignatureAsync(
        SignatureRequest request,
        ISignatureCloudStorageClient storage,
        CancellationToken ct
    )
    {
        if (request.PreparerSignatureFileId is not { } fileId || request.PreparerFields.Count == 0)
            return null;

        var download = await storage.DownloadAsync(request.TenantId, fileId, ct);
        return download.IsSuccess ? download.Value : null;
    }

    private static SealingResult ApplySeal(
        SignatureRequest request,
        DateTime evidenceAtUtc,
        RequestDocument document,
        byte[] originalBytes,
        IReadOnlyDictionary<Guid, byte[]> signatureImages,
        byte[]? preparerImage,
        IDocumentSealingEngine sealer
    )
    {
        var fields = BuildFieldRenders(request, document.Id, signatureImages, preparerImage);
        // Sin el id de la SignatureRequest: es un identificador interno sensible y no debe estamparse en
        // cada página del documento firmado. La integridad ya la ancla el "Doc SHA-256" del pie, y la
        // referencia del envelope vive en el Certificate of Completion (documento aparte).
        var footer = $"Completed {evidenceAtUtc:yyyy-MM-dd HH:mm} UTC";
        var sealingRequest = new SealingRequest(
            originalBytes,
            fields,
            document.DocumentHashPre?.Value ?? string.Empty,
            footer
        );
        return sealer.Seal(sealingRequest);
    }

    private static IReadOnlyList<SealedFieldRender> BuildFieldRenders(
        SignatureRequest request,
        Guid documentId,
        IReadOnlyDictionary<Guid, byte[]> signatureImages,
        byte[]? preparerImage
    )
    {
        var renders = new List<SealedFieldRender>();
        foreach (var signer in request.Signers)
        {
            var signedAt = signer.SignedAtUtc ?? DateTime.UtcNow;
            // Solo los campos de firma llevan la imagen; un campo de texto/fecha del mismo firmante
            // conserva su render tipográfico aunque exista PNG de firma.
            signatureImages.TryGetValue(signer.Id, out var signerImage);
            foreach (var field in signer.Fields.Where(field => field.DocumentId == documentId))
            {
                var textValue =
                    field.Kind == SignatureFieldKind.Text
                        ? signer.FieldValues.FirstOrDefault(v => v.FieldId == field.Id)?.Value
                        : null;
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
                        SignedAtUtc: signedAt,
                        SignatureImageBytes: field.Kind == SignatureFieldKind.Signature ? signerImage : null,
                        Value: textValue
                    )
                );
            }
        }

        // Campos del preparador (canal paralelo): se estampan con su firma reutilizable al sellar, nunca
        // antes → el firmante no la ve. Sin imagen (perfil sin subir/baja fallida) → sello tipográfico.
        var preparerName = request.Preparer?.DisplayName ?? "Preparer";
        var preparerSignedAt = request.PreparerSignedAtUtc ?? request.CompletedAtUtc ?? DateTime.UtcNow;
        foreach (var field in request.PreparerFields.Where(field => field.DocumentId == documentId))
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
                    SignerDisplayName: preparerName,
                    SignedAtUtc: preparerSignedAt,
                    SignatureImageBytes: field.Kind == SignatureFieldKind.Signature ? preparerImage : null,
                    Value: null
                )
            );
        }
        return renders;
    }

    private static SignatureFileUpload BuildSealedUpload(
        SignatureRequest request,
        RequestDocument document,
        SealingResult sealResult
    )
    {
        var (ownerType, ownerId) = ResolveSealedOwner(request);
        return new(
            Content: sealResult.SealedPdfBytes,
            FileName: BuildDocumentFileName(document.Title, "_Signed.pdf"),
            ContentType: "application/pdf",
            // Values must match CloudStorage's OwnerType / FolderType enums.
            OwnerType: ownerType,
            OwnerId: ownerId,
            FolderType: "Signatures",
            TaxYear: (request.CompletedAtUtc ?? request.CreatedAtUtc).Year,
            ActorId: request.CreatedByUserId
        );
    }

    /// <summary>Dueno del documento firmado — ver <see cref="SealedDocumentOwner"/> (politica testeable).</summary>
    private static (string OwnerType, Guid OwnerId) ResolveSealedOwner(SignatureRequest request) =>
        SealedDocumentOwner.Resolve(request.Signers.Select(signer => signer.MappedCustomerId).ToList(), request.Id);

    // ============== Fase 3b: certificate opcional ==============

    /// <summary>
    /// Genera SOLO los bytes del Certificate of Completion (incluye la descarga del logo de oficina/sistema,
    /// que es la parte lenta), SIN subirlo. La subida se hace luego, junto a la del sellado, justo antes del
    /// commit — ver el comentario de ORDEN CRÍTICO en <see cref="SealAndPersistAsync"/>. Devuelve null si la
    /// request no pidió certificado.
    /// </summary>
    private static async Task<Result<IReadOnlyList<CertificateRenderArtifact>>> GenerateCertificateArtifactsAsync(
        SignatureRequest request,
        ICertificateOfCompletionRenderer renderer,
        ISignatureCloudStorageClient storage,
        ITenantBrandingRefRepository brandingRepository,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (!request.GenerateCertificate)
            return Result.Success<IReadOnlyList<CertificateRenderArtifact>>([]);

        // Logo de la OFICINA: la marca del tenant dueño del request (TenantBrandingRef). Al lado, el logo
        // del SISTEMA: la marca del tenant plataforma (jturbi), misma fuente. Si la oficina no tiene logo
        // queda solo el del sistema; si el sistema tampoco, el renderer cae al logo de plataforma embebido.
        var (issuerName, officeLogo) = await ResolveBrandingAsync(
            request.TenantId,
            brandingRepository,
            storage,
            logger,
            ct
        );

        byte[]? platformLogo = null;
        if (request.TenantId != PlatformTenant.Id)
        {
            var (_, resolvedPlatformLogo) = await ResolveBrandingAsync(
                PlatformTenant.Id,
                brandingRepository,
                storage,
                logger,
                ct
            );
            platformLogo = resolvedPlatformLogo;
        }

        if (request.CertificateGenerationMode == CertificateGenerationMode.SingleForRequest)
        {
            if (request.CertificateFileId is not null)
                return Result.Success<IReadOnlyList<CertificateRenderArtifact>>([]);

            var model = BuildCertificateModel(request, issuerName, platformLogo, officeLogo, documentId: null);
            var rendered = renderer.Render(model);
            return Result.Success<IReadOnlyList<CertificateRenderArtifact>>([
                new CertificateRenderArtifact(null, request.Title, rendered.CertificatePdfBytes),
            ]);
        }

        var artifacts = request
            .Documents.Where(document => document.CertificateFileId is null)
            .OrderBy(document => document.Order)
            .Select(document =>
            {
                var model = BuildCertificateModel(request, issuerName, platformLogo, officeLogo, document.Id);
                return new CertificateRenderArtifact(
                    document.Id,
                    document.Title,
                    renderer.Render(model).CertificatePdfBytes
                );
            })
            .ToList();
        return Result.Success<IReadOnlyList<CertificateRenderArtifact>>(artifacts);
    }

    private static SignatureFileUpload BuildCertificateUpload(
        SignatureRequest request,
        string title,
        byte[] certificateBytes
    )
    {
        var (ownerType, ownerId) = ResolveSealedOwner(request);
        return new(
            Content: certificateBytes,
            FileName: BuildDocumentFileName(title, "_Certificate.pdf"),
            ContentType: "application/pdf",
            OwnerType: ownerType,
            OwnerId: ownerId,
            FolderType: "Signatures",
            TaxYear: (request.CompletedAtUtc ?? request.CreatedAtUtc).Year,
            ActorId: request.CreatedByUserId
        );
    }

    private const int MaxFileNameBaseLength = 120;

    /// <summary>Nombre de descarga profesional desde el Title (translitera acentos, deja [A-Za-z0-9._-]). No toca el ObjectKey.</summary>
    private static string BuildDocumentFileName(string title, string suffix)
    {
        var normalized = (title ?? string.Empty).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastWasUnderscore = false;
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.')
            {
                sb.Append(ch);
                lastWasUnderscore = false;
            }
            else if (!lastWasUnderscore)
            {
                sb.Append('_');
                lastWasUnderscore = true;
            }
        }

        var cleaned = sb.ToString().Trim('_', '.', '-');
        if (cleaned.Length > MaxFileNameBaseLength)
            cleaned = cleaned[..MaxFileNameBaseLength].Trim('_', '.', '-');
        if (string.IsNullOrEmpty(cleaned))
            cleaned = "Document";
        return cleaned + suffix;
    }

    private static async Task<(string? IssuerName, byte[]? TenantLogo)> ResolveBrandingAsync(
        Guid tenantId,
        ITenantBrandingRefRepository brandingRepository,
        ISignatureCloudStorageClient storage,
        ILogger logger,
        CancellationToken ct
    )
    {
        var branding = await brandingRepository.GetByTenantIdAsync(tenantId, ct);
        if (branding is null)
            return (null, null);

        var issuerName = string.IsNullOrWhiteSpace(branding.OfficeName) ? null : branding.OfficeName;
        if (!branding.HasLogo)
            return (issuerName, null);

        // Best-effort: un logo que no baja no debe frenar la generación del certificado.
        var logoResult = await storage.DownloadAsync(tenantId, branding.LogoFileId!.Value, ct);
        if (logoResult.IsFailure)
        {
            logger.LogWarning(
                "Tenant logo download failed for the certificate (tenant {TenantId}); rendering without it.",
                tenantId
            );
            return (issuerName, null);
        }

        return (issuerName, logoResult.Value);
    }

    private static CertificateOfCompletionModel BuildCertificateModel(
        SignatureRequest request,
        string? issuerName,
        byte[]? platformLogo,
        byte[]? tenantLogo,
        Guid? documentId
    )
    {
        var documents = request
            .Documents.Where(document => documentId is null || document.Id == documentId.Value)
            .OrderBy(document => document.Order)
            .ToList();
        var participatingSignerIds = documents
            .SelectMany(document =>
                request.Signers.Where(signer => signer.Fields.Any(field => field.DocumentId == document.Id))
            )
            .Select(signer => signer.Id)
            .ToHashSet();

        return new(
            SignatureRequestId: request.Id,
            Title: documentId is null ? request.Title : documents[0].Title,
            Category: request.Category,
            CreatedAtUtc: request.CreatedAtUtc,
            CompletedAtUtc: request.CompletedAtUtc ?? DateTime.UtcNow,
            SignersGlobal: request
                .Signers.Where(s => documentId is null || participatingSignerIds.Contains(s.Id))
                .Select(s => new CertificateSignerEntry(
                    s.Id,
                    s.FullName.Value,
                    s.Email.Value,
                    s.Order,
                    s.Status,
                    s.FirstViewedAtUtc,
                    s.ConsentAcceptedAtUtc,
                    s.SignedAtUtc,
                    s.ClientIp,
                    s.UserAgent
                ))
                .ToList(),
            Documents: documents
                .Select(document => new CertificateDocumentEntry(
                    document.Id,
                    document.Order,
                    document.Title,
                    document.DocumentHashPre?.Value ?? string.Empty,
                    document.DocumentHashPost?.Value ?? string.Empty,
                    document.SealedAtUtc ?? request.CompletedAtUtc ?? DateTime.UtcNow,
                    request
                        .Signers.Where(signer => signer.Fields.Any(field => field.DocumentId == document.Id))
                        .Select(signer => new CertificateDocumentSignerEntry(
                            signer.Id,
                            signer.FullName.Value,
                            signer
                                .DocumentCompletions.FirstOrDefault(completion => completion.DocumentId == document.Id)
                                ?.CompletedAtUtc
                                ?? signer.SignedAtUtc
                                ?? request.CompletedAtUtc
                                ?? DateTime.UtcNow,
                            signer.ClientIp,
                            signer.UserAgent
                        ))
                        .ToList()
                ))
                .ToList(),
            IssuerName: issuerName,
            PlatformLogo: platformLogo,
            TenantLogo: tenantLogo,
            // Referencia del preparador (ERO): solo si firmó internamente. Identificador enmascarado, sin imagen.
            Preparer: BuildPreparerEntry(request)
        );
    }

    private static CertificatePreparerEntry? BuildPreparerEntry(SignatureRequest request)
    {
        // Un acta legal referencia partes por IDENTIDAD REAL (así lo hacen DocuSign/Adobe): solo se
        // incluye al preparador si su identidad 8879 (PreparerInfo: nombre + PTIN) está fijada. Una firma
        // "My Signature" estampada sin identidad NO genera una entrada con nombre inventado — la firma
        // visual vive en el documento; el acta lista partes identificadas.
        if (request.Preparer is not { } preparer)
            return null;

        var stampApplied = request.PreparerSignatureFileId is not null && request.PreparerFields.Count > 0;
        if (!request.IsPreparerSigned && !stampApplied)
            return null;

        var at = request.PreparerSignedAtUtc ?? request.CompletedAtUtc;
        return new CertificatePreparerEntry(preparer.DisplayName, MaskIdentifier(preparer.PtinOrEfin), at);
    }

    /// <summary>Enmascara un PTIN/EFIN dejando visibles los últimos 4 (p. ej. "P•••••5678").</summary>
    private static string MaskIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return string.Empty;
        if (identifier.Length <= 4)
            return new string('•', identifier.Length);
        return identifier[0] + new string('•', identifier.Length - 5) + identifier[^4..];
    }

    // ============== Fase 6: publicar Sealed / Failed ==============

    private static async Task PublishSealedAsync(
        SignatureRequest request,
        PipelineOutcome outcome,
        string correlationId,
        IMessageBus bus
    )
    {
        var finalSealedDocumentId = outcome.RequestSealingCompleted
            ? outcome.SealedDocuments.LastOrDefault()?.DocumentId
            : null;
        foreach (var sealedDocument in outcome.SealedDocuments)
        {
            await bus.PublishAsync(
                new SignatureDocumentSealedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlationId,
                    IdempotencyKey = $"signature.doc_sealed:{request.Id:N}:{sealedDocument.DocumentId:N}:v1",
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    DocumentId = sealedDocument.DocumentId,
                    SealedFileId = sealedDocument.SealedFileId,
                    DocumentHashPost = sealedDocument.HashPost,
                    CertificateFileId =
                        outcome.DocumentCertificateFileIds.TryGetValue(
                            sealedDocument.DocumentId,
                            out var documentCertificateFileId
                        )
                            ? documentCertificateFileId
                        : sealedDocument.DocumentId == finalSealedDocumentId ? outcome.CertificateFileId
                        : null,
                    SealedAtUtc = sealedDocument.SealedAtUtc,
                }
            );
        }

        if (!outcome.RequestSealingCompleted)
            return;

        await bus.PublishAsync(
            new SignatureRequestSealingCompletedIntegrationEvent
            {
                TenantId = request.TenantId,
                CorrelationId = correlationId,
                SignatureRequestId = request.Id,
                CreatedByUserId = request.CreatedByUserId,
                DocumentCount = request.Documents.Count,
                SealedAtUtc = outcome.CompletedAtUtc,
            }
        );
    }

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
                    CreatedByUserId = request.CreatedByUserId,
                    Reason = error.Message,
                    ErrorCode = error.Code,
                    FailedAtUtc = DateTime.UtcNow,
                }
            )
            .AsTask();

    // ============== Helpers ==============

    private static string ResolveCorrelationId(string? sourceCorrelationId, Guid sourceEventId) =>
        string.IsNullOrWhiteSpace(sourceCorrelationId) ? sourceEventId.ToString("N") : sourceCorrelationId;
}
