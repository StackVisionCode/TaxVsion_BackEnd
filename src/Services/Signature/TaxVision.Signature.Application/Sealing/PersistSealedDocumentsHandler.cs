using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Delivery;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Wolverine;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// SIG-6, fase B: en una sola transacción registra lo sellado y publica por el outbox <c>SaveFileRequested</c>,
/// <c>DocumentSealed</c> y el cierre. Así <c>FileAvailable</c> nunca llega antes del commit. La línea
/// <c>RequestSealed</c> de la auditoría sigue saliendo de <c>DocumentSealed</c>.
/// </summary>
public static class PersistSealedDocumentsHandler
{
    public static async Task Handle(
        PersistSealedDocumentsCommand command,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<PersistSealedDocumentsCommand> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(command.CorrelationId))
        {
            var request = await repository.GetByIdAsync(command.TenantId, command.SignatureRequestId, ct);
            if (request is null)
            {
                logger.LogWarning(
                    "Sealing results dropped: SignatureRequest {RequestId} not found.",
                    command.SignatureRequestId
                );
                return;
            }

            var applied = Apply(request, command);
            if (applied.Superseded)
                logger.LogWarning(
                    "Sealing run of request {RequestId} lost against another run; its files stay unregistered.",
                    request.Id
                );

            // Reentrega de lo ya guardado: no hay un segundo hecho que avisar.
            if (applied.Nothing && applied.Failure is null)
            {
                await RequestDiscardedCertificatesAsync(request, applied, command.CorrelationId, bus);
                return;
            }

            if (applied.Failure is not null)
                request.MarkSealingFailed(DateTime.UtcNow);

            await unitOfWork.SaveChangesAsync(ct);

            foreach (var file in command.FileRequests.Where(file => applied.FileIds.Contains(file.FileId)))
                await bus.PublishAsync(file);

            var completed =
                applied.Failure is null
                && request.Status == SignatureRequestStatus.Completed
                && request.AllDocumentsSealed()
                && request.AllCertificateArtifactsGenerated();
            await PublishSealedAsync(request, applied.Documents, completed, command.CorrelationId, bus);

            if (applied.Failure is { } failure)
            {
                await PublishFailedAsync(request, failure, command.CorrelationId, bus);
                return;
            }

            await RequestDiscardedCertificatesAsync(request, applied, command.CorrelationId, bus);
            if (!completed)
                return;

            await bus.PublishAsync(
                new SignatureRequestSealingCompletedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = command.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    DocumentCount = request.Documents.Count,
                    SealedAtUtc = DateTime.UtcNow,
                }
            );

            // Varios clientes firmantes: cada uno recibe su copia en el portal.
            if (request.PendingCustomerCopies().Count > 0)
                await bus.PublishAsync(new CreateCustomerPortalCopiesCommand(request.TenantId, request.Id));
        }
    }

    private sealed record AppliedResults(
        IReadOnlyList<SealedDocumentResult> Documents,
        IReadOnlySet<Guid> FileIds,
        bool Superseded,
        Error? Failure
    )
    {
        public bool Nothing => FileIds.Count == 0;
    }

    private static AppliedResults Apply(SignatureRequest request, PersistSealedDocumentsCommand command)
    {
        var documents = new List<SealedDocumentResult>();
        var fileIds = new HashSet<Guid>();
        var superseded = false;
        var failure = command.FailureCode is { } code ? new Error(code, command.FailureMessage ?? code) : null;

        foreach (var result in command.Documents)
        {
            var document = request.Documents.FirstOrDefault(candidate => candidate.Id == result.DocumentId);
            // Otra corrida ya selló este documento: su archivo y su hash mandan, MarkDocumentSealed los pisaría.
            if (document?.SealedFileId is { } current)
            {
                superseded |= current != result.SealedFileId;
                continue;
            }

            var marked = MarkSealed(request, result);
            if (marked.IsFailure)
            {
                failure ??= marked.Error;
                break;
            }
            documents.Add(result);
            fileIds.Add(result.SealedFileId);
        }

        // El certificado atestigua los hashes de su corrida: si esta perdió, no vale para los guardados.
        if (!superseded)
            foreach (var certificate in command.Certificates)
            {
                if (HasCertificate(request, certificate))
                    continue;

                var recorded = certificate.DocumentId is { } documentId
                    ? request.RecordDocumentCertificate(documentId, certificate.CertificateFileId)
                    : request.RecordCertificate(certificate.CertificateFileId);
                if (recorded.IsFailure)
                {
                    failure ??= recorded.Error;
                    break;
                }
                fileIds.Add(certificate.CertificateFileId);
            }

        return new AppliedResults(documents, fileIds, superseded, failure);
    }

    private static Result MarkSealed(SignatureRequest request, SealedDocumentResult result)
    {
        var hash = DocumentHash.Create(result.HashPost);
        return hash.IsFailure
            ? Result.Failure(hash.Error)
            : request.MarkDocumentSealed(result.DocumentId, result.SealedFileId, hash.Value, result.SealedAtUtc);
    }

    private static bool HasCertificate(SignatureRequest request, SealedCertificateResult certificate) =>
        certificate.DocumentId is { } documentId
            ? request.Documents.Any(document => document.Id == documentId && document.CertificateFileId is not null)
            : request.CertificateFileId is not null;

    // El job de reconciliación solo mira documentos sin sellar: un certificado descartado se vuelve a pedir aquí.
    private static async Task RequestDiscardedCertificatesAsync(
        SignatureRequest request,
        AppliedResults applied,
        string correlationId,
        IMessageBus bus
    )
    {
        if (
            !applied.Superseded
            || request.Status != SignatureRequestStatus.Completed
            || !request.AllDocumentsSealed()
            || request.AllCertificateArtifactsGenerated()
        )
            return;

        var now = DateTime.UtcNow;
        await bus.PublishAsync(
            new SignatureDocumentsReadyForSealingIntegrationEvent
            {
                TenantId = request.TenantId,
                CorrelationId = correlationId,
                SignatureRequestId = request.Id,
                CreatedByUserId = request.CreatedByUserId,
                DocumentIds = [],
                ReadyAtUtc = now,
                IdempotencyKey = $"signature.documents_ready:{request.Id:N}:certificate:{now:yyyyMMddHHmmssfff}",
            }
        );
    }

    private static async Task PublishSealedAsync(
        SignatureRequest request,
        IReadOnlyList<SealedDocumentResult> documents,
        bool completed,
        string correlationId,
        IMessageBus bus
    )
    {
        var finalDocumentId = completed ? documents.LastOrDefault()?.DocumentId : null;
        foreach (var sealedDocument in documents)
        {
            var document = request.Documents.First(candidate => candidate.Id == sealedDocument.DocumentId);
            await bus.PublishAsync(
                new SignatureDocumentSealedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlationId,
                    IdempotencyKey = SignatureSealingIdempotencyKeys.DocumentSealed(
                        request.Id,
                        sealedDocument.DocumentId,
                        sealedDocument.SealedFileId
                    ),
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    DocumentId = sealedDocument.DocumentId,
                    SealedFileId = sealedDocument.SealedFileId,
                    DocumentHashPost = sealedDocument.HashPost,
                    CertificateFileId =
                        document.CertificateFileId
                        ?? (sealedDocument.DocumentId == finalDocumentId ? request.CertificateFileId : null),
                    SealedAtUtc = sealedDocument.SealedAtUtc,
                    ExternalReference = request.ExternalReference.ToMessaging(),
                }
            );
        }
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
}
