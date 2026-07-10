using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
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
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var resolution = await PublicTokenResolver.ResolveAsync(cmd.Token, tokenService, repository, ct);
        if (resolution.IsFailure)
            return Result.Failure(resolution.Error);

        var (request, signer) = (resolution.Value.Request, resolution.Value.Signer);
        var evidenceValidation = ValidateEvidence(cmd, signer);
        if (evidenceValidation.IsFailure)
            return evidenceValidation;

        var signedAt = DateTime.UtcNow;
        var sign = request.MarkSignerSigned(
            signer.Id,
            signedAt,
            cmd.Method,
            cmd.TypedName,
            cmd.SignatureImageFileId,
            cmd.ClientIp,
            cmd.UserAgent
        );
        if (sign.IsFailure)
            return sign;

        await unitOfWork.SaveChangesAsync(ct);
        await PublishSignedAsync(request, signer, signedAt, cmd.ClientIp, correlation, bus);
        if (request.Status == SignatureRequestStatus.Completed)
            await PublishCompletedAsync(request, correlation, bus);
        return Result.Success();
    }

    // ------------------------------------------------------------------
    // Métodos privados: una única responsabilidad por método
    // ------------------------------------------------------------------

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
                    SignedAtUtc = signedAtUtc,
                    TotalSignersCount = request.Signers.Count,
                    SignedSignersCount = SignedCount(request),
                    IsRequestCompleted = request.Status == SignatureRequestStatus.Completed,
                    ClientIp = clientIp,
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
                    CompletedAtUtc = request.CompletedAtUtc ?? DateTime.UtcNow,
                    OriginalFileId = request.OriginalFileId,
                    DocumentHashPre = request.DocumentHashPre!.Value,
                    SignerIds = request.Signers.Select(s => s.Id).ToList(),
                    GenerateCertificate = request.GenerateCertificate,
                }
            )
            .AsTask();
}
