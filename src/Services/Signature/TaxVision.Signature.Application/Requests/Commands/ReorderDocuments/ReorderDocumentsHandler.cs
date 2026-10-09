using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Commands.ReorderDocuments;

public static class ReorderDocumentsHandler
{
    public static async Task<Result> Handle(
        ReorderDocumentsCommand cmd,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure(
                new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
            );

        var reordered = request.ReorderDocuments(cmd.OrderedDocumentIds);
        if (reordered.IsFailure)
            return reordered;

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        return Result.Success();
    }
}
