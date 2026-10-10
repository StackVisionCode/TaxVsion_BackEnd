using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.ReplaceDocumentFile;

public static class ReplaceDocumentFileHandler
{
    public static async Task<Result<ReplaceDocumentFileResponse>> Handle(
        ReplaceDocumentFileCommand cmd,
        ISignatureRequestRepository repository,
        IFileMetadataRefRepository fileRepository,
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

        // Reengancha el DocumentHashPre con el checksum del nuevo archivo si CloudStorage ya lo tiene
        // disponible; si no, queda null hasta que llegue el FileAvailable y lo rellenemos por otra vía.
        var file = await fileRepository.GetByFileIdAsync(cmd.TenantId, cmd.NewFileId, ct);
        if (file is { Status: FileScanStatus.Available, ChecksumSha256: not null })
        {
            var hash = DocumentHash.Create(file.ChecksumSha256);
            if (hash.IsSuccess)
                request.AttachDocumentHash(cmd.DocumentId, hash.Value);
        }

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
