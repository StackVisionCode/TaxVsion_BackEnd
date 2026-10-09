using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// El firmante envía su firma. Fases: (1) validar token + epoch, (2) validar evidencia
/// según CaptureMethod (Typed → nombre coincidente, Drawn/Uploaded → FileId presente),
/// (3) mutar aggregate con captura completa, (4) persistir, (5) publicar Signed y
/// — si aplica — Completed.
/// </summary>
public static class SubmitSignatureHandler
{
    public static async Task<Result> Handle(
        SubmitSignatureCommand cmd,
        ISigningTokenService tokenService,
        ISignatureRequestRepository repository,
        IJtiDenylist denylist,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var resolution = await PublicTokenResolver.ResolveAsync(cmd.Token, tokenService, repository, denylist, ct);
        if (resolution.IsFailure)
            return Result.Failure(resolution.Error);

        var (request, signer) = (resolution.Value.Request, resolution.Value.Signer);
        var evidenceValidation = ValidateEvidence(cmd, signer);
        if (evidenceValidation.IsFailure)
            return evidenceValidation;

        var signedAt = DateTime.UtcNow;
        var documentIds = ResolveDocumentIds(cmd.DocumentIds, signer);
        if (documentIds.Count == 0)
            return Result.Failure(
                new Error("Signature.Public.DocumentsRequired", "There are no pending documents to complete.")
            );
        var signerWasSigned = signer.Status == SignerStatus.Signed;

        // P4: anclar los valores de los campos de texto ANTES de firmar (el aggregate valida
        // propiedad del campo, tipo Text y requeridos). Un fallo aquí aborta la firma.
        var captureValues = request.CaptureSignerFieldValues(
            signer.Id,
            documentIds,
            MapFieldValues(cmd.FieldValues),
            signedAt
        );
        if (captureValues.IsFailure)
            return captureValues;

        var sign = request.MarkSignerDocumentsCompleted(
            signer.Id,
            documentIds,
            signedAt,
            cmd.Method,
            cmd.TypedName,
            cmd.SignatureImageFileId,
            cmd.ClientIp,
            cmd.UserAgent
        );
        if (sign.IsFailure)
            return Result.Failure(sign.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(request.TenantId, ct);
        var readyDocumentIds = sign
            .Value.Where(documentId =>
                request.IsDocumentReadyForSealing(documentId)
                && request.Documents.Any(document => document.Id == documentId && document.SealedFileId is null)
            )
            .Distinct()
            .ToList();
        if (readyDocumentIds.Count > 0)
            await PublishDocumentsReadyForSealingAsync(request, readyDocumentIds, signedAt, correlation, bus);
        if (!signerWasSigned && signer.Status == SignerStatus.Signed)
            await PublishSignedAsync(request, signer, signedAt, cmd.ClientIp, correlation, bus);
        // F7 — si el aggregate dejó enganchada la copia parcial, dispara el pipeline de delivery.
        if (
            sign.Value.Count > 0
            && request.SendPartialCopyOnEachSignature
            && request.PartialCopyAudience.Includes(signer.Id)
        )
            await PublishPartialCopyRequestedAsync(request, signer, sign.Value, signedAt, correlation, bus);
        if (request.Status == SignatureRequestStatus.Completed)
            await PublishCompletedAsync(request, correlation, bus);
        return Result.Success();
    }

    private static Task PublishDocumentsReadyForSealingAsync(
        SignatureRequest request,
        IReadOnlyList<Guid> documentIds,
        DateTime readyAtUtc,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureDocumentsReadyForSealingIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    DocumentIds = documentIds,
                    ReadyAtUtc = readyAtUtc,
                    IdempotencyKey =
                        $"signature.documents_ready:{request.Id:N}:{string.Join('-', documentIds.Order().Select(id => id.ToString("N")))}:v1",
                }
            )
            .AsTask();

    private static Task PublishPartialCopyRequestedAsync(
        SignatureRequest request,
        Signer signer,
        IReadOnlyList<Guid> documentIds,
        DateTime completedAtUtc,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignerPartialCopyRequestedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    SignerId = signer.Id,
                    DocumentIds = documentIds,
                    SignedAtUtc = completedAtUtc,
                    // v1 inicial; cada "resend" del preparador incrementa el sufijo.
                    IdempotencyKey = BuildPartialCopyBatchKey(request.Id, signer.Id, documentIds),
                }
            )
            .AsTask();

    // ------------------------------------------------------------------
    // Métodos privados: una única responsabilidad por método
    // ------------------------------------------------------------------

    private static IReadOnlyList<SignerFieldValueInput> MapFieldValues(IReadOnlyList<SubmitFieldValueDto>? values) =>
        values is null ? [] : values.Select(v => new SignerFieldValueInput(v.FieldId, v.Value)).ToList();

    private static IReadOnlyList<Guid> ResolveDocumentIds(IReadOnlyList<Guid>? requested, Signer signer) =>
        requested is { Count: > 0 }
            ? requested.Distinct().ToList()
            : signer
                .Fields.Select(field => field.DocumentId)
                .Distinct()
                .Where(documentId => signer.DocumentCompletions.All(item => item.DocumentId != documentId))
                .ToList();

    private static string BuildPartialCopyBatchKey(Guid requestId, Guid signerId, IReadOnlyList<Guid> documentIds) =>
        $"signature.partial_copy:{requestId:N}:{signerId:N}:{string.Join('-', documentIds.Order().Select(id => id.ToString("N")))}:v1";

    private static Result ValidateEvidence(SubmitSignatureCommand cmd, Signer signer) =>
        cmd.Method switch
        {
            SignatureCaptureMethod.Typed => ValidateTypedName(cmd.TypedName, signer),
            SignatureCaptureMethod.Drawn or SignatureCaptureMethod.Uploaded => ValidateImageFileId(
                cmd.SignatureImageFileId
            ),
            _ => Result.Failure(new Error("Signature.Public.UnknownMethod", "Unknown signature capture method.")),
        };

    private static Result ValidateTypedName(string? typedName, Signer signer)
    {
        if (string.IsNullOrWhiteSpace(typedName))
            return Result.Failure(
                new Error("Signature.Public.TypedNameEmpty", "Typed name is required for Typed method.")
            );

        var normalizedTyped = typedName.Trim();
        var normalizedExpected = signer.FullName.Value.Trim();
        if (!string.Equals(normalizedTyped, normalizedExpected, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(
                new Error("Signature.Public.TypedNameMismatch", "Typed name must match the signer full name.")
            );
        return Result.Success();
    }

    private static Result ValidateImageFileId(Guid? fileId)
    {
        if (fileId is null || fileId == Guid.Empty)
            return Result.Failure(
                new Error(
                    "Signature.Public.ImageRequired",
                    "SignatureImageFileId is required for Drawn/Uploaded methods."
                )
            );
        return Result.Success();
    }

    private static int SignedCount(SignatureRequest request) =>
        request.Signers.Count(s => s.Status == SignerStatus.Signed);

    private static Task PublishSignedAsync(
        SignatureRequest request,
        Signer signer,
        DateTime signedAtUtc,
        string? clientIp,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new DocumentSignedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    SignerId = signer.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    SignedAtUtc = signedAtUtc,
                    TotalSignersCount = request.Signers.Count,
                    SignedSignersCount = SignedCount(request),
                    IsRequestCompleted = request.Status == SignatureRequestStatus.Completed,
                    ClientIp = clientIp,
                    MappedCustomerId = signer.MappedCustomerId,
                }
            )
            .AsTask();

    private static Task PublishCompletedAsync(
        SignatureRequest request,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestCompletedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    CompletedAtUtc = request.CompletedAtUtc ?? DateTime.UtcNow,
                    Documents = request
                        .Documents.Select(document => new DocumentHashDescriptor(
                            document.Id,
                            document.OriginalFileId,
                            document.DocumentHashPre!.Value
                        ))
                        .ToList(),
                    SignerIds = request.Signers.Select(s => s.Id).ToList(),
                    GenerateCertificate = request.GenerateCertificate,
                    Signers = request
                        .Signers.Select(s => new SignerContactSnapshot(
                            s.Id,
                            s.Email.Value,
                            s.FullName.Value,
                            s.Language,
                            s.Order,
                            s.MappedCustomerId,
                            s.PhoneNumber?.Value,
                            SignerChannelResolver.PreferredChannelFor(s)
                        ))
                        .ToList(),
                }
            )
            .AsTask();
}
