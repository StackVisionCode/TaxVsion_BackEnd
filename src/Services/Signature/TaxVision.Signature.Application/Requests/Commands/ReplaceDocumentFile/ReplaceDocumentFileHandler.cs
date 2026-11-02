using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Documents.Verification;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.ReplaceDocumentFile;

public static class ReplaceDocumentFileHandler
{
    public static async Task<Result<ReplaceDocumentFileResponse>> Handle(
        ReplaceDocumentFileCommand cmd,
        ISignatureRequestRepository repository,
        IRequestDocumentVerifier verifier,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure<ReplaceDocumentFileResponse>(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var replace = request.ReplaceDocumentFile(cmd.DocumentId, cmd.NewFileId, cmd.NewPageCount);
        if (replace.IsFailure)
            return Result.Failure<ReplaceDocumentFileResponse>(replace.Error);

        var outcome = replace.Value;

        // Si CloudStorage ya tiene el nuevo archivo disponible se revisa ahora; si no, queda pendiente hasta
        // que llegue el FileAvailable. Un archivo inválido no reemplaza al anterior.
        var verification = await verifier.VerifyAsync(request, cmd.DocumentId, ct);
        if (verification.Kind == DocumentVerificationKind.Rejected)
            return Result.Failure<ReplaceDocumentFileResponse>(
                DocumentVerificationErrors.Rejected(
                    request.Documents.Single(document => document.Id == cmd.DocumentId),
                    verification.Rejection!
                )
            );

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);

        var replacedAt = DateTime.UtcNow;
        await bus.PublishAsync(
            new SignatureDocumentReplacedIntegrationEvent
            {
                TenantId = request.TenantId,
                CorrelationId = correlation.CorrelationId,
                SignatureRequestId = request.Id,
                DocumentId = cmd.DocumentId,
                OldFileId = outcome.OldFileId,
                NewFileId = outcome.NewFileId,
                OldPageCount = outcome.OldPageCount,
                NewPageCount = outcome.NewPageCount,
                FieldsInvalidated = outcome.FieldsInvalidated,
                ReplacedAtUtc = replacedAt,
            }
        );

        return Result.Success(
            new ReplaceDocumentFileResponse(
                cmd.DocumentId,
                outcome.OldFileId,
                outcome.NewFileId,
                outcome.OldPageCount,
                outcome.NewPageCount,
                outcome.FieldsInvalidated
            )
        );
    }
}

/// <summary>
/// Devuelve el resultado del reemplazo para que la UI muestre un aviso explícito si cayeron campos.
/// </summary>
public sealed record ReplaceDocumentFileResponse(
    Guid DocumentId,
    Guid OldFileId,
    Guid NewFileId,
    int? OldPageCount,
    int? NewPageCount,
    int FieldsInvalidated
);
