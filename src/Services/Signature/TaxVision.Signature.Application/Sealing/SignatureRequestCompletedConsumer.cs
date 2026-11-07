using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Fase A del sellado (SIG-6), sin transacción: descarga, sella, genera el certificado y deja los archivos en MinIO.
/// No toca el agregado. Termina con <see cref="PersistSealedDocumentsCommand"/>, que registra todo en una sola
/// transacción y publica por el outbox.
/// <list type="number">
///   <item>Carga la solicitud y verifica precondiciones.</item>
///   <item>Descarga el PDF original, aplica el sello y lo deja en MinIO.</item>
///   <item>Con todo sellado, genera el Certificate of Completion y lo deja en MinIO.</item>
///   <item>Entrega el resultado a la fase B, también el fallo definitivo.</item>
/// </list>
/// Cada corrida usa <c>FileId</c> nuevos: la cuenta de MinIO solo puede escribir, y pisar una clave ya entregada
/// dejaría un hash guardado que no es el del archivo. Lo que sube una corrida fallida queda en el bucket temporal,
/// sin registrar. Un fallo transitorio lanza <see cref="SealingTransientException"/> y Wolverine reintenta; uno
/// permanente (o el transitorio del último intento) lo resuelve la fase B. El trabajo lo hacen
/// <see cref="SignatureImageScanGate"/>, <see cref="SealingDocumentProcessor"/> y <see cref="SealingCertificateBuilder"/>.
/// </summary>
public static class SignatureRequestCompletedConsumer
{
    private static readonly TimeSpan SealingLockTtl = TimeSpan.FromMinutes(10);

    [NonTransactional]
    public static Task Handle(
        SealCompletedRequestCommand command,
        ISignatureRequestRepository repository,
        SignatureImageScanGate imageGate,
        SealingDocumentProcessor documents,
        SealingCertificateBuilder certificates,
        IDistributedLock distributedLock,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        Envelope envelope,
        CancellationToken ct
    ) =>
        HandleCore(
            command.TenantId,
            command.SignatureRequestId,
            command.DocumentIds,
            command.CompletedAtUtc,
            command.CorrelationId,
            command.SourceEventId,
            repository,
            imageGate,
            documents,
            certificates,
            distributedLock,
            bus,
            correlation,
            logger,
            envelope.Attempts,
            ct
        );

    [NonTransactional]
    public static Task Handle(
        SignatureDocumentsReadyForSealingIntegrationEvent evt,
        ISignatureRequestRepository repository,
        SignatureImageScanGate imageGate,
        SealingDocumentProcessor documents,
        SealingCertificateBuilder certificates,
        IDistributedLock distributedLock,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        Envelope envelope,
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
            imageGate,
            documents,
            certificates,
            distributedLock,
            bus,
            correlation,
            logger,
            envelope.Attempts,
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
        SignatureImageScanGate imageGate,
        SealingDocumentProcessor documents,
        SealingCertificateBuilder certificates,
        IDistributedLock distributedLock,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        int attempt,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(sourceCorrelationId, sourceEventId);
        using (correlation.Push(correlationId))
        {
            // Lock por request para evitar que dos réplicas del worker sellen la misma
            // solicitud en paralelo (double-processing en clúster). Si otro nodo lo tomó, se
            // reintenta (SIG-3); el idempotency-check del pipeline evita duplicados.
            await using var lockHandle = await distributedLock.AcquireAsync(
                SealingLockKey(signatureRequestId),
                SealingLockTtl,
                ct
            );
            if (!lockHandle.IsAcquired)
                throw new SealingLockBusyException(
                    $"Another node holds the sealing lock for request {signatureRequestId}."
                );

            var request = await LoadOrSkipAsync(tenantId, signatureRequestId, repository, logger, ct);
            if (request is null)
                return;

            // SIG-3: también los documentos listos que no pidió el mensaje (por si se perdió el suyo).
            var documentIds = SealableDocumentIds(request, requestedDocumentIds);

            // Gate anti-carrera con ClamAV: si algún PNG de firma aún no pasó el scan (proyección
            // ausente/Pending), lanza SignatureImageNotReadyException → Wolverine redelivera con
            // cooldown hasta que llegue FileAvailable. Devuelve los firmantes cuya imagen ya está
            // Available (los Infected/Deleted se excluyen: sellan con fallback tipográfico).
            var readyImageSignerIds = await imageGate.ResolveAsync(request, documentIds, evidenceAtUtc, ct);
            var run = new SealingRun();

            // SIG-14: la imagen no existe en CloudStorage; esperar no la trae.
            if (readyImageSignerIds.IsFailure)
            {
                await bus.PublishAsync(run.ToCommand(request, correlationId, readyImageSignerIds.Error));
                return;
            }

            var pipeline = await SealAndStageAsync(
                request,
                documentIds,
                evidenceAtUtc,
                readyImageSignerIds.Value,
                run,
                documents,
                certificates,
                ct
            );

            // Transitorio: Wolverine vuelve a entregar el mensaje; no se registró nada.
            if (
                pipeline.IsFailure
                && SignatureStorage.IsTransient(pipeline.Error)
                && attempt < SealingTransientException.FinalAttempt
            )
                throw new SealingTransientException(
                    $"Sealing of request {request.Id} failed transiently ({pipeline.Error.Code}); attempt {attempt} of {SealingTransientException.FinalAttempt}."
                );

            await bus.PublishAsync(run.ToCommand(request, correlationId, pipeline.IsFailure ? pipeline.Error : null));
        }
    }

    private static async Task<SignatureRequest?> LoadOrSkipAsync(
        Guid tenantId,
        Guid signatureRequestId,
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

        var hasReadyDocument = request.Documents.Any(document =>
            document.SealedFileId is null && request.IsDocumentReadyForSealing(document.Id)
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

    // Los pedidos más todos los listos y sin sellar de la solicitud.
    private static IReadOnlyList<Guid> SealableDocumentIds(
        SignatureRequest request,
        IReadOnlyList<Guid> requestedDocumentIds
    ) =>
        requestedDocumentIds
            .Concat(
                request
                    .Documents.Where(document =>
                        document.SealedFileId is null && request.IsDocumentReadyForSealing(document.Id)
                    )
                    .Select(document => document.Id)
            )
            .Distinct()
            .ToList();

    // Con todo sellado y la solicitud completa, el certificado; un documento suelto todavía no lo lleva.
    private static async Task<Result> SealAndStageAsync(
        SignatureRequest request,
        IReadOnlyList<Guid> requestedDocumentIds,
        DateTime evidenceAtUtc,
        IReadOnlySet<Guid> readyImageSignerIds,
        SealingRun run,
        SealingDocumentProcessor documents,
        SealingCertificateBuilder certificates,
        CancellationToken ct
    )
    {
        var sealedDocuments = await documents.SealAndStageAsync(
            request,
            requestedDocumentIds,
            evidenceAtUtc,
            readyImageSignerIds,
            run,
            ct
        );
        if (sealedDocuments.IsFailure)
            return sealedDocuments;

        var requestSealingCompleted =
            request.Status == SignatureRequestStatus.Completed
            && request.Documents.All(document =>
                document.SealedFileId is not null || run.Find(document.Id) is not null
            );
        if (!requestSealingCompleted)
            return Result.Success();

        return await certificates.GenerateAndStageAsync(request, run, ct);
    }

    /// <summary>Lock por solicitud del sellado; lo comparte quien toque sus archivos finales.</summary>
    internal static string SealingLockKey(Guid signatureRequestId) => $"signature:sealing:{signatureRequestId:N}";

    private static string ResolveCorrelationId(string? sourceCorrelationId, Guid sourceEventId) =>
        string.IsNullOrWhiteSpace(sourceCorrelationId) ? sourceEventId.ToString("N") : sourceCorrelationId;
}
