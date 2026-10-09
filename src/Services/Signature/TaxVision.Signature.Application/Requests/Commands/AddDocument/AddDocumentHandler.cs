using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.AddDocument;

public static class AddDocumentHandler
{
    public static async Task<Result<SignatureRequestDocumentResponse>> Handle(
        AddDocumentCommand cmd,
        ISignatureRequestRepository repository,
        IFileMetadataRefRepository fileRepository,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure<SignatureRequestDocumentResponse>(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var added = request.AddDocument(cmd.OriginalFileId, cmd.Title, cmd.Note, cmd.PageCount);
        if (added.IsFailure)
            return Result.Failure<SignatureRequestDocumentResponse>(added.Error);

        var file = await fileRepository.GetByFileIdAsync(cmd.TenantId, cmd.OriginalFileId, ct);
        if (file is { Status: FileScanStatus.Available, ChecksumSha256: not null })
        {
            var hash = DocumentHash.Create(file.ChecksumSha256);
            if (hash.IsSuccess)
                request.AttachDocumentHash(added.Value.Id, hash.Value);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);

        var document = added.Value;
        return Result.Success(
            new SignatureRequestDocumentResponse(
                document.Id,
                document.Order,
                document.Title,
                document.OriginalFileId,
                document.DocumentHashPre?.Value,
                document.SealedFileId,
                document.CertificateFileId,
                document.DocumentHashPost?.Value,
                document.SealedAtUtc,
                document.Note
            )
        );
    }
}
